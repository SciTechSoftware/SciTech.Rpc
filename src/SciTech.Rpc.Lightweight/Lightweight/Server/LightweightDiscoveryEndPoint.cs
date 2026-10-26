#region Copyright notice and license
// Copyright (c) SciTech Software AB and contributors.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using Microsoft.Extensions.Logging;
using SciTech.Diagnostics;
using SciTech.Rpc.Lightweight.Internal;
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

        /// <summary>
        /// Initializes a new discovery end point, which provides <paramref name="connectionInfo"/> as the suggested connection
        /// to discovery clients.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown if the display name is longer than 128 UTF-8 bytes, or the host URL longer than 256 bytes.
        /// The connection info is included in discovery responses, which must fit in a single unfragmented datagram.</exception>
        public LightweightDiscoveryEndPoint(RpcConnectionInfo connectionInfo, ILogger? logger = null)
        {
            if (connectionInfo is null) throw new ArgumentNullException(nameof(connectionInfo));

            if (ServiceDiscoveryOperations.GetUtf8Size(connectionInfo.DisplayName) > ServiceDiscoveryOperations.MaxDisplayNameSize)
            {
                throw new ArgumentException($"The display name of a discovery end point must not exceed {ServiceDiscoveryOperations.MaxDisplayNameSize} UTF-8 bytes.", nameof(connectionInfo));
            }

            if (ServiceDiscoveryOperations.GetUtf8Size(connectionInfo.HostUrl?.ToString()) > ServiceDiscoveryOperations.MaxHostUrlSize)
            {
                throw new ArgumentException($"The host URL of a discovery end point must not exceed {ServiceDiscoveryOperations.MaxHostUrlSize} UTF-8 bytes.", nameof(connectionInfo));
            }

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

        /// <summary>
        /// Gets or sets the address families that discovery requests are received on. <c>null</c> (the default) means
        /// both IPv4 and IPv6. Internal to allow testing a single address family.
        /// </summary>
        internal IReadOnlyCollection<AddressFamily>? AddressFamilies { get; set; }

        /// <summary>
        /// Gets the network interfaces that discovery requests are sent and received on, together with one address
        /// for each address family of the interface (in <paramref name="addressFamilies"/>, or IPv4 and IPv6 if
        /// <c>null</c>). An interface with both IPv4 and IPv6 addresses is returned once for each family.
        /// </summary>
        /// <remarks>
        /// <see cref="IPInterfaceProperties.MulticastAddresses"/> must not be used to select the interfaces, since it
        /// is always empty on Linux.
        /// </remarks>
        internal static IEnumerable<(NetworkInterface NetworkInterface, IPAddress Address)> GetDiscoveryInterfaces(
            IReadOnlyCollection<AddressFamily>? addressFamilies = null)
        {
            bool includeIPv4 = addressFamilies?.Contains(AddressFamily.InterNetwork) ?? true;
            bool includeIPv6 = (addressFamilies?.Contains(AddressFamily.InterNetworkV6) ?? true) && Socket.OSSupportsIPv6;

            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus == OperationalStatus.Up
                    && networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback
                    && networkInterface.SupportsMulticast)
                {
                    var unicastAddresses = networkInterface.GetIPProperties()?.UnicastAddresses;
                    if (unicastAddresses == null)
                    {
                        continue;
                    }

                    if (includeIPv4
                        && unicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address is IPAddress ipv4Address)
                    {
                        yield return (networkInterface, ipv4Address);
                    }

                    if (includeIPv6)
                    {
                        // A non link-local address is preferred, since it can be used in the connection URL of a
                        // discovered server without a zone index (see LightweightDiscoveryClient).
                        var ipv6Addresses = unicastAddresses
                            .Select(a => a.Address)
                            .Where(a => a.AddressFamily == AddressFamily.InterNetworkV6 && !a.IsIPv6Teredo)
                            .ToList();
                        var ipv6Address = ipv6Addresses.FirstOrDefault(a => !a.IsIPv6LinkLocal) ?? ipv6Addresses.FirstOrDefault();
                        if (ipv6Address != null)
                        {
                            yield return (networkInterface, ipv6Address);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gets the discovery multicast group for the address family of <paramref name="ipAddress"/>.
        /// </summary>
        internal static IPAddress GetDiscoveryGroup(IPAddress ipAddress)
            => ipAddress.AddressFamily == AddressFamily.InterNetwork ? DefaultMulticastAddress : DefaultMulticastAddressV6;

        /// <summary>
        /// Creates a UDP client for discovery on the address family of <paramref name="ipAddress"/>.
        /// </summary>
        internal static UdpClient CreateDiscoveryClient(IPAddress ipAddress)
        {
            var udpClient = new UdpClient(ipAddress.AddressFamily);
            if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
            {
                // The IPv6 socket must not receive IPv4 traffic, or it would conflict with the IPv4 discovery sockets.
                // .NET already disables dual mode for sockets created for an address family (also on Linux, where
                // the OS default is dual mode), but this makes the requirement explicit.
                udpClient.Client.DualMode = false;
            }

            return udpClient;
        }

        internal static void JoinDiscoveryGroup(UdpClient udpClient, NetworkInterface networkInterface, IPAddress ipAddress)
        {
            if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
            {
                udpClient.JoinMulticastGroup(DefaultMulticastAddress, ipAddress);
            }
            else
            {
                // An IPv6 group is joined on the interface index; the local address overload only supports IPv4.
                udpClient.JoinMulticastGroup(GetIPv6InterfaceIndex(networkInterface), DefaultMulticastAddressV6);
            }
        }

        /// <summary>
        /// Makes multicasts from <paramref name="udpClient"/> go out on <paramref name="networkInterface"/>. Otherwise
        /// they're sent on the default multicast interface, which may be, e.g., a VPN adapter.
        /// </summary>
        internal static void SetMulticastInterface(UdpClient udpClient, NetworkInterface networkInterface, IPAddress ipAddress)
        {
            if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
            {
                udpClient.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, ipAddress.GetAddressBytes());
            }
            else
            {
                udpClient.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface, GetIPv6InterfaceIndex(networkInterface));
            }
        }

        private static int GetIPv6InterfaceIndex(NetworkInterface networkInterface)
            => networkInterface.GetIPProperties().GetIPv6Properties().Index;

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
                this.logger?.LogInformation("Starting LightweightDiscoveryEndpoint listener.");
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

                this.logger?.LogInformation("LightweightDiscoveryEndpoint listener started.");
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

            private List<UdpClient> RetrieveUdpClients()
            {
                List<UdpClient> udpClients = new();
                foreach (var (networkInterface, ipAddress) in GetDiscoveryInterfaces(this.endPoint.AddressFamilies))
                {
                    var udpClient = CreateDiscoveryClient(ipAddress);
                    try
                    {
                        var socket = udpClient.Client;
                        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
                        socket.Bind(new IPEndPoint(ipAddress.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, DefaultDiscoveryPort));
                        JoinDiscoveryGroup(udpClient, networkInterface, ipAddress);

                        udpClients.Add(udpClient);
                    }
                    catch (SocketException x)
                    {
                        this.logger?.LogInformation(x, "Failed to listen for discovery requests on network interface {NetworkInterface}.", networkInterface.Name);
                        udpClient.Dispose();
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
                using (this.logger?.BeginScope("DiscoveryRpcListener.RunListener begin"))
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
                                        var udpClient = udpClients[index];
                                        this.logger?.LogTrace("Begin receive discovery UDP at {EndPoint}.", udpClient.Client?.LocalEndPoint);

#if NET
                                        receiveTasks[index] = udpClient.ReceiveAsync(cancellationToken).AsTask();
#else
                                        receiveTasks[index] = udpClient.ReceiveAsync();
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
                                    this.logger?.LogTrace("Discovery UDP request received at {EndPoint}.", currentClient.Client?.LocalEndPoint);

                                    var responseData = await this.connectionHandler.HandleDatagramAsync(this.endPoint, res.Buffer, cancellationToken).ContextFree();
                                    if (responseData != null)
                                    {
                                        this.logger?.LogTrace("Sending discovery UDP response to {EndPoint}.", res.RemoteEndPoint);
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
                            catch (Exception x) when (!x.IsFatal() && !(x is ObjectDisposedException) && !cancellationToken.IsCancellationRequested)
                            {
                                // Anyone on the network can send datagrams, so a datagram that can't be handled must not end
                                // the listener.
                                this.logger?.LogInformation(x, "Failed to handle discovery datagram.");
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
                        this.logger?.LogInformation("Closing UDP discovery listener {UdpClient}", udpClient);
                        udpClient?.Close();
                    }
                }

                if (listenerCts != null && listenerTask != null)
                {
                    listenerCts.Cancel();
                    try
                    {
                        this.logger?.LogInformation("Waiting for discovery listener to finish.");
                        await listenerTask.ContextFree();
                        this.logger?.LogInformation("Discovery listener finished.");
                    }
                    catch (Exception x )
                    {
                        this.logger?.LogWarning(x, "Discovery listener error when stopping.");
                        throw;
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
                    } else
                    {
                        this.logger?.LogInformation("No UDP clients found for discovery listener. No network?");
                    }
                }
            }
        }
    }
}
