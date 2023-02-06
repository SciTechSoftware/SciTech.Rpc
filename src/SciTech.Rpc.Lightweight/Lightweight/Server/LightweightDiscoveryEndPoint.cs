using Microsoft.Extensions.Logging;
using SciTech.Rpc.Lightweight.Server.Internal;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server
{
    public class LightweightDiscoveryEndPoint : LightweightRpcEndPoint
    {
        public const int DefaultDiscoveryPort = 39159;
        public static readonly IPAddress DefaultMulticastAddress = IPAddress.Parse("239.255.250.129");
        public static readonly IPAddress DefaultMulticastAddressV6 = IPAddress.Parse("ff18::0732");

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores")]
        public static readonly IPAddress DefaultMulticastAddressV6_2 = IPAddress.Parse("ff18::9832");

        private readonly RpcConnectionInfo connectionInfo;

        private readonly ILogger? logger;

        public LightweightDiscoveryEndPoint(RpcConnectionInfo connectionInfo, ILogger? logger = null)
        {
            this.connectionInfo = connectionInfo;
            this.logger = logger;
        }

        public override string HostName => this.connectionInfo.HostUrl?.Host ?? "";

        public override string DisplayName => this.connectionInfo.DisplayName;

        public override RpcConnectionInfo GetConnectionInfo(RpcServerId serverId)
            => this.connectionInfo.SetServerId(serverId);

        protected internal override ILightweightRpcListener CreateListener(
            IRpcConnectionHandler connectionHandler,
            int maxRequestSize, int maxResponseSize)
        {
            return new DiscoveryRpcListener(this, connectionHandler, this.logger);
        }

        private protected class DiscoveryRpcListener : ILightweightRpcListener
        {
            private readonly IRpcConnectionHandler connectionHandler;
            private readonly LightweightDiscoveryEndPoint endPoint;
            private readonly ILogger? logger;

            private readonly object syncRoot = new object();
            private bool hasPendingListenerUpdate;
            private bool isListening;
            private CancellationTokenSource? listenerCts;

            private Task? listenerTask;

            private List<UdpClient>? udpClients;
            private bool updatingListener;

            public DiscoveryRpcListener(LightweightDiscoveryEndPoint endPoint, IRpcConnectionHandler connectionHandler, ILogger? logger)
            {
                this.endPoint = endPoint;
                this.connectionHandler = connectionHandler;
                this.logger = logger;
            }

            public ValueTask DisposeAsync()
            {
                return new ValueTask(this.StopAsync());
            }

            public void Listen()
            {
                lock (this.syncRoot)
                {
                    if (this.isListening)
                    {
                        throw new InvalidOperationException($"{nameof(DiscoveryRpcListener)} is already listening.");
                    }

                    this.isListening = true;
                }

                NetworkChange.NetworkAvailabilityChanged += this.NetworkChange_NetworkAvailabilityChanged;
                NetworkChange.NetworkAddressChanged += this.NetworkChange_NetworkAddressChanged;
                this.UpdateListenerAsync().Forget();
            }

            public async Task StopAsync()
            {
                lock (this.syncRoot)
                {
                    if (!this.isListening) return;
                }

                NetworkChange.NetworkAvailabilityChanged -= this.NetworkChange_NetworkAvailabilityChanged;
                NetworkChange.NetworkAddressChanged -= this.NetworkChange_NetworkAddressChanged;

                await this.StopListenerAsync().ContextFree();

                this.listenerCts?.Cancel();

                if (this.udpClients != null)
                {
                    foreach (var udpClient in this.udpClients)
                    {
                        udpClient?.Close();
                    }
                }

                var listenerTask = this.listenerTask;
                this.listenerTask = null;

                if (listenerTask != null)
                {
                    await listenerTask.ContextFree();
                }

                this.listenerCts?.Dispose();
                this.listenerCts = null;

                this.udpClients = null;
            }

            private static List<UdpClient> RetrieveUdpClients()
            {
                List<UdpClient> udpClients = new();
                var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var networkInterface in networkInterfaces)
                {
                    if (networkInterface.OperationalStatus == OperationalStatus.Up
                        && networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && networkInterface.SupportsMulticast)
                    {
                        var ipProperties = networkInterface.GetIPProperties();
                        if (ipProperties != null && ipProperties.MulticastAddresses.Count > 0 && ipProperties.UnicastAddresses.Count > 0)
                        {
                            var ipAddress =
                                (ipProperties.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                                ?? ipProperties.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6))?.Address;

                            if (ipAddress != null)
                            {
                                if (ipAddress.AddressFamily == AddressFamily.InterNetwork || ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
                                {
                                    var udpClient = new UdpClient(ipAddress.AddressFamily);// ;
                                    var socket = udpClient.Client;
                                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
                                    if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
                                    {
                                        socket.Bind(new IPEndPoint(IPAddress.Any, DefaultDiscoveryPort));
                                        udpClient.JoinMulticastGroup(DefaultMulticastAddress, ipAddress);
                                    }
                                    else
                                    {
                                        socket.Bind(new IPEndPoint(IPAddress.IPv6Any, DefaultDiscoveryPort));
                                        udpClient.JoinMulticastGroup(DefaultMulticastAddressV6, ipAddress);
                                    }

                                    udpClients.Add(udpClient);
                                }
                            }
                        }
                    }
                }

                return udpClients;
            }

            private void NetworkChange_NetworkAddressChanged(object? sender, EventArgs e)
            {
                this.UpdateListenerAsync().Forget();
            }

            private void NetworkChange_NetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
            {
                this.UpdateListenerAsync().Forget();
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "RunListener should be silent.")]
            private async Task RunListener(IReadOnlyList<UdpClient> udpClients, CancellationToken cancellationToken)
            {
                using (this.logger?.BeginScope("DiscoveryRpcListener.RunListener begin at"))// {EndPoint}.", udpClient.Client.LocalEndPoint))
                {
                    try
                    {
                        Task<UdpReceiveResult>?[] receiveTasks = new Task<UdpReceiveResult>[udpClients.Count];

                        while (!cancellationToken.IsCancellationRequested)
                        {
                            try
                            {
                                for (int index = 0; index < receiveTasks.Length; index++)
                                {
                                    if (receiveTasks[index] == null)
                                    {
#if NET6_0_OR_GREATER
                                        receiveTasks[index] = udpClients[index].ReceiveAsync(cancellationToken).AsTask();
#else
                                        receiveTasks[index] = udpClients[index].ReceiveAsync();
#endif
                                    }
                                }

                                var receiveTask = await Task.WhenAny(receiveTasks!).ContextFree();

                                int receiveIndex = Array.IndexOf(receiveTasks, receiveTask);
                                if (receiveIndex < 0) throw new InvalidOperationException("Unexpected receive task finished");

                                var currentClient = udpClients[receiveIndex];
                                receiveTasks[receiveIndex] = null;

                                if (!cancellationToken.IsCancellationRequested)
                                {
                                    var res = receiveTask.AwaiterResult();
                                    var responseData = await this.connectionHandler.HandleDatagramAsync(this.endPoint, res.Buffer, cancellationToken).ContextFree();
                                    if (responseData != null)
                                    {
                                        await currentClient.SendAsync(responseData, responseData.Length, res.RemoteEndPoint).ContextFree();
                                    }
                                }
                            }
                            catch (SocketException x)
                            {
                                if (!cancellationToken.IsCancellationRequested)
                                {
                                    // TODO: Check ErrorCode and maybe add a delay, to avoid tight retry loop. And maybe it is necessary to recreate UdpCient?
                                    this.logger?.LogInformation(x, "Failed to send or receive from discovery UDP client. Retrying.");
                                }
                            }
                        }
                    }
                    catch (Exception x)
                    {
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            this.logger?.LogError(x, $"Error occurred when running {nameof(DiscoveryRpcListener)}.");
                        }
                    }

                    this.logger?.LogInformation("DiscoveryRpcListener.RunListener end.");
                }
            }

            private async Task StopListenerAsync()
            {
                CancellationTokenSource? listenerCts;
                Task? listenerTask;
                List<UdpClient>? udpClients;

                lock (this.syncRoot)
                {
                    listenerCts = this.listenerCts;
                    listenerTask = this.listenerTask;
                    udpClients = this.udpClients;

                    this.listenerTask = null;
                    this.listenerCts = null;
                    this.udpClients = null;
                }

                if (udpClients != null)
                {
                    foreach (var udpClient in udpClients)
                    {
                        udpClient?.Close();
                    }
                }

                if (listenerCts != null && listenerTask != null)
                {
                    listenerCts.Cancel();
                    try
                    {
                        await listenerTask.ContextFree();
                    }
                    finally
                    {
                        listenerCts.Dispose();
                    }
                }
            }

            private async Task UpdateListenerAsync()
            {
                lock (this.syncRoot)
                {
                    if (!this.isListening) return;

                    this.hasPendingListenerUpdate = true;
                    if (this.updatingListener) return;

                    this.updatingListener = true;
                }

                while (true)
                {
                    lock (this.syncRoot)
                    {
                        if (!this.hasPendingListenerUpdate)
                        {
                            this.updatingListener = false;
                            return;
                        }

                        this.hasPendingListenerUpdate = false;
                    }

                    await this.StopListenerAsync().ContextFree();

                    List<UdpClient> udpClients = RetrieveUdpClients();

                    if (udpClients.Count > 0)
                    {
                        lock (this.syncRoot)
                        {
                            if (this.isListening)
                            {
                                this.udpClients = udpClients;
                                this.listenerCts = new CancellationTokenSource();
                                var ct = this.listenerCts.Token;
                                this.listenerTask = Task.Run(() => this.RunListener(udpClients, ct));
                            }
                            else
                            {
                                foreach (var udpClient in udpClients)
                                {
                                    udpClient.Dispose();
                                }
                            }
                        }
                    }
                }
            }

            //private async Task HandleDiscoveryRequest(LightweightRpcFrame request, IPEndPoint remoteEndPoint, UdpClient udpClient)
            //{
            //    switch (request.FrameType)
            //    {
            //        case RpcFrameType.ServiceDiscoveryRequest:
            //            switch (request.RpcOperation)
            //            {
            //                case "SciTech.Rpc.LightweightServiceDiscovery.GetPublishedSingletons":
            //                    var publishedSingletons = this.discoveryProvider.GetPublishedSingletons();
            //                    var response = new RpcPublishedSingletonsResponse
            //                    {
            //                        ConnectionInfo = this.connectionInfo,
            //                        Services = publishedSingletons.Select(ps => new RpcPublishedSingleton { ServiceName = ps }).ToArray()
            //                    };

            //                    var responseFrame = new LightweightRpcFrame(RpcFrameType.ServiceDiscoveryResponse, request.MessageNumber, request.RpcOperation, null);

            //                    using (var writer = new BufferWriterStreamImpl())
            //                    {
            //                        var writeState = responseFrame.BeginWrite(writer);

            //                        using (var jsonWriter = new Utf8JsonWriter((IBufferWriter<byte>)writer))
            //                        {
            //                            JsonSerializer.Serialize(jsonWriter, response, typeof(RpcPublishedSingletonsResponse));
            //                        }

            //                        LightweightRpcFrame.EndWrite((int)writer.Length, writeState);

            //                        var responseData = writer.ToArray();

            //                        await udpClient.SendAsync(responseData, responseData.Length, remoteEndPoint).ContextFree();
            //                    }

            //                    break;
            //            }
            //            break;
            //        default:
            //            // We only handle discovery requests, and since we're using UDP noone caller is expecting
            //            // that responses may be lost. So let's just ignore unknown frames.
            //            break;
            //    }
            //}
        }
    }
}
