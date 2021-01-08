using Microsoft.Extensions.Logging;
using SciTech.Rpc.Lightweight.Server.Internal;
using SciTech.Threading;
using System;
using System.Buffers;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server
{
    public class LightweightDiscoveryEndPoint : LightweightRpcEndPoint
    {
        public static readonly IPAddress DefaultMulticastAddress = IPAddress.Parse("239.255.250.129");
        public static readonly IPAddress DefaultMulticastAddressV6 = IPAddress.Parse("ff18::0732");
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores")]
        public static readonly IPAddress DefaultMulticastAddressV6_2 = IPAddress.Parse("ff18::9832");
        public const int DefaultDiscoveryPort = 39159;

        private readonly RpcServerConnectionInfo connectionInfo;

        private readonly ILogger? logger;

        public LightweightDiscoveryEndPoint(RpcServerConnectionInfo connectionInfo, ILogger? logger = null)
        {
            this.connectionInfo = connectionInfo;
            this.logger = logger;
        }

        public override string DisplayName => this.connectionInfo.DisplayName;

        public override string HostName => this.connectionInfo.HostUrl?.Host ?? "";

        public override RpcServerConnectionInfo GetConnectionInfo(RpcServerId serverId)
            => this.connectionInfo.SetServerId(serverId);

        protected internal override ILightweightRpcListener CreateListener(
            IRpcConnectionHandler connectionHandler,
            int maxRequestSize, int maxResponseSize)
        {
            return new DiscoveryRpcListener(this, connectionHandler, this.logger);
        }

        private protected class DiscoveryRpcListener : ILightweightRpcListener
        {
            private readonly LightweightDiscoveryEndPoint endPoint;

            private readonly IRpcConnectionHandler connectionHandler;
            
            private readonly ILogger? logger;

            private CancellationTokenSource? listenerCts;

            private Task? listenerTask;

            private UdpClient? udpClient;

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
                if (this.udpClient != null)
                {
                    throw new InvalidOperationException($"{nameof(DiscoveryRpcListener)} is already listening.");
                }

                var udpClient = this.udpClient = new UdpClient(AddressFamily.InterNetwork);
                var socket = udpClient.Client;
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
                socket.Bind(new IPEndPoint(IPAddress.Any, DefaultDiscoveryPort));
                udpClient.JoinMulticastGroup(DefaultMulticastAddress);

                //var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                //foreach( var networkInterface in networkInterfaces)
                //{
                //    if( networkInterface.OperationalStatus == OperationalStatus.Up 
                //        && networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback
                //        && networkInterface.SupportsMulticast )
                //    {
                //        int ifIndex = networkInterface.GetIPProperties().GetIPv4Properties().Index;
                //        this.udpClient.JoinMulticastGroup(DefaultMulticastAddress);

                //        //don't set this socket option if the socket is bound to the 
                //        //loopback adapter because it will throw an argument exception.
                //        //if (!isLoopbackAdapter)
                //        //{
                //        //    socket.SetSocketOption(ipOptionLevel, SocketOptionName.MulticastLoopback, allowMulticastLoopback);
                //        //}

                //        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
                //        //this.udpClient.JoinMulticastGroup(DefaultMulticastAddressV6);
                //        //this.udpClient.JoinMulticastGroup(DefaultMulticastAddressV6_2);
                //        break;
                //    }
                //}

                

                this.listenerCts = new CancellationTokenSource();
                this.listenerTask = Task.Run(() => this.RunListener(udpClient, this.listenerCts.Token));
            }

            public async Task StopAsync()
            {
                this.listenerCts?.Cancel();

                this.udpClient?.Close();

                var listenerTask = this.listenerTask;
                this.listenerTask = null;

                if (listenerTask != null)
                {
                    await listenerTask.ContextFree();
                }

                this.listenerCts?.Dispose();
                this.listenerCts = null;

                this.udpClient = null;

            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "RunListener should be silent.")]
            private async Task RunListener(UdpClient udpClient, CancellationToken cancellationToken)
            {
                using (this.logger?.BeginScope("DiscoveryRpcListener.RunListener begin at {EndPoint}.", udpClient.Client.LocalEndPoint))
                {
                    try
                    {
                        while (!cancellationToken.IsCancellationRequested)
                        {
                            try
                            {
                                var res = await udpClient.ReceiveAsync().ContextFree();
                                if (!cancellationToken.IsCancellationRequested)
                                {
                                    var responseData = await this.connectionHandler.HandleDatagramAsync(this.endPoint, res.Buffer, cancellationToken).ContextFree();
                                    if (responseData != null)
                                    {
                                        await udpClient.SendAsync(responseData, responseData.Length, res.RemoteEndPoint).ContextFree();
                                    }
                                }
                            }
                            catch (Exception)
                            {
                                // TODO: Should SocketException be handled and optionally retried?
                                throw;
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
