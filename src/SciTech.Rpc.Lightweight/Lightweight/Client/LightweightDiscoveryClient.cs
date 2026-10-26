using Microsoft.Extensions.Logging;
using SciTech.Collections.Immutable;
using SciTech.Diagnostics;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Lightweight.Server;
using SciTech.Rpc.Logging;
using SciTech.Rpc.Serialization;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Client
{
    public sealed class DiscoveredServerEventArgs : EventArgs
    {
        internal DiscoveredServerEventArgs(RpcConnectionInfo connectionInfo)
        {
            this.DiscoveredServer = connectionInfo;
        }

        public RpcConnectionInfo DiscoveredServer { get; }
    }

    public sealed class DiscoveredService
    {
        internal DiscoveredService(RpcConnectionInfo connectionInfo, string service/*, int version*/)
        {
            this.ConnectionInfo = connectionInfo;
            this.Service = service;
            this.LastDiscoveryTime = DateTime.UtcNow;
            //this.Version = version;
        }

        public RpcConnectionInfo ConnectionInfo { get; }

        public string Service { get; }

        // Not implemented yet.
        //public int Version { get; }

        internal DateTime LastDiscoveryTime { get; set; }
    }

    public sealed class DiscoveredServiceEventArgs : EventArgs
    {
        internal DiscoveredServiceEventArgs(DiscoveredService discoveredService)
        {
            this.DiscoveredService = discoveredService;
        }

        public DiscoveredService DiscoveredService { get; }
    }

    public class LightweightDiscoveryClient
    { 
        private const int MaxDiscoveryFrameSize = 65535;

        private readonly Dictionary<RpcServerId, DiscoveredServer> discoveredServers = new Dictionary<RpcServerId, DiscoveredServer>();

        private readonly ILogger? logger;

        private readonly object syncRoot = new object();

        private SynchronizationContext? syncContext;
        private Guid clientId;
        private bool findingServices;

        private ImmutableArrayList<DiscoveredService>? discoveredServices;

        public LightweightDiscoveryClient( ILogger? logger = null)
        {
            this.logger = logger ?? RpcLogger.TryCreateLogger<LightweightDiscoveryClient>();
        }

        /// <summary>
        /// Occurs when a new RPC server has been discovered.
        /// <note>Will be invoked in the synchronization context of the <see cref="FindServicesAsync(CancellationToken)"/> caller.</note>
        /// </summary>
        public event EventHandler<DiscoveredServerEventArgs>? ServerDiscovered;

        /// <summary>
        /// Occurs when a previously discovered RPC server is no longer available.
        /// <note>Will be invoked in the synchronization context of the <see cref="FindServicesAsync(CancellationToken)"/> caller.</note>
        /// </summary>
        public event EventHandler<DiscoveredServerEventArgs>? ServerLost;

        /// <summary>
        /// Occurs when a new RPC service has been discovered.
        /// <note>Will be invoked in the synchronization context of the <see cref="FindServicesAsync(CancellationToken)"/> caller.</note>
        /// </summary>
        public event EventHandler<DiscoveredServiceEventArgs>? ServiceDiscovered;

        /// <summary>
        /// Occurs when a previously discovered RPC service is no longer available.
        /// <note>Will be invoked in the synchronization context of the <see cref="FindServicesAsync(CancellationToken)"/> caller.</note>
        /// </summary>
        public event EventHandler<DiscoveredServiceEventArgs>? ServiceLost;

        public event EventHandler? ServicesChanged;

        /// <summary>
        /// Gets the RPC services discovered during the current or last call to <see cref="FindServicesAsync(CancellationToken)"/>.
        /// </summary>
        public ImmutableArrayList<DiscoveredService> DiscoveredServices
        {
            get
            {
                lock( this.syncRoot )
                {
                    if( this.discoveredServices == null )
                    {
                        var builder = ImmutableArray.CreateBuilder<DiscoveredService>();
                        builder.AddRange(discoveredServers.Values.SelectMany(s => s.Services));
                        this.discoveredServices = builder.ToImmutableArrayList();
                    }

                    return this.discoveredServices;
                }
            }
        }

        public TimeSpan ServiceDiscoveryTimeout { get; set; } = TimeSpan.FromSeconds( 5 );

        /// <summary>
        /// Gets or sets the address families that discovery requests are sent on. <c>null</c> (the default) means
        /// both IPv4 and IPv6. Internal to allow testing a single address family.
        /// </summary>
        internal IReadOnlyCollection<AddressFamily>? AddressFamilies { get; set; }

        public async Task<ImmutableArrayList<DiscoveredService>> FindServicesAsync(CancellationToken cancellationToken)
        {
            if( this.findingServices)
            {
                // To simplify things a bit.
                throw new InvalidOperationException($"Only a single {nameof(FindServicesAsync)} call can be running at the time.");
            }

            this.findingServices = true;
            this.syncContext = SynchronizationContext.Current;
            this.clientId = Guid.NewGuid();

            try
            {
                Task receiverTask;

                List<UdpClient> udpClients = new List<UdpClient>();
                List<IPEndPoint> discoveryEndPoints = new List<IPEndPoint>();
                foreach (var (networkInterface, ipAddress) in LightweightDiscoveryEndPoint.GetDiscoveryInterfaces(this.AddressFamilies))
                {
                    var udpClient = LightweightDiscoveryEndPoint.CreateDiscoveryClient(ipAddress);
                    try
                    {
                        // Responses are sent to the bound address. The client doesn't need to join the discovery group,
                        // it only sends to it.
                        udpClient.Client.Bind(new IPEndPoint(ipAddress, 0));
                        LightweightDiscoveryEndPoint.SetMulticastInterface(udpClient, networkInterface, ipAddress);

                        udpClients.Add(udpClient);
                        discoveryEndPoints.Add(new IPEndPoint(LightweightDiscoveryEndPoint.GetDiscoveryGroup(ipAddress), LightweightDiscoveryEndPoint.DefaultDiscoveryPort));
                    }
                    catch (SocketException x)
                    {
                        this.logger?.LogInformation(x, "Failed to send discovery requests on network interface {NetworkInterface} ({Address}).", networkInterface.Name, ipAddress);
                        udpClient.Dispose();
                    }
                }

                if( udpClients.Count == 0)
                {
                    // TODO: Use NetworkChange.NetworkAvailabilityChanged to update uppClients
                    // until cancelled.
                    return ImmutableArrayList<DiscoveredService>.Empty;
                }

                try
                {
                    receiverTask = this.RunReceiver(udpClients, cancellationToken);

                    using var frameWriter = new LightweightRpcFrameWriter(MaxDiscoveryFrameSize);

                    int nextRequestNo = 1;
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        int requestNo = nextRequestNo++;
                        var requestHeader = new LightweightRpcFrame(
                            RpcFrameType.UnaryRequest, requestNo,
                            ServiceDiscoveryOperations.GetPublishedSingletons, null);

                        var requestData = frameWriter.WriteFrame(requestHeader, new RpcDiscoveryRequest(clientId), ServiceDiscoveryOperations.DiscoverySerializer);

                        for (int index = 0; index < udpClients.Count; index++)
                        {
                            await SendRequestAsync(udpClients[index], discoveryEndPoints[index], requestData).ContextFree();
                            if (cancellationToken.IsCancellationRequested) break;
                        }

                        var finishedTask = await Task.WhenAny(Task.Delay(1000, cancellationToken), receiverTask).ContextFree();
                        if (finishedTask == receiverTask)
                        {
                            // Probably an error in the receiver. Stop Find.
                            break;
                        }

                        List<DiscoveredServer>? expiredServers = null;

                        lock (this.syncRoot)
                        {
                            DateTime now = DateTime.UtcNow;
                            DateTime expiryTime = now - this.ServiceDiscoveryTimeout;

                            foreach (var server in this.discoveredServers.Values)
                            {
                                if (server.HasExpired(expiryTime))
                                {
                                    expiredServers ??= new();
                                    expiredServers.Add(server);
                                }
                            }

                            if (expiredServers != null)
                            {
                                foreach( var server in expiredServers)
                                {
                                    this.discoveredServers.Remove(server.ConnectionInfo.ServerId);
                                }
                            }
                        }

                        if ( expiredServers != null)
                        {
                            this.discoveredServices = null;
                            foreach (var server in expiredServers)
                            {
                                this.UpdateDiscoveredServices(server.ConnectionInfo, null, server.Services);
                                this.ServerLost?.Invoke(this, new DiscoveredServerEventArgs(server.ConnectionInfo));
                            }

                            this.ServicesChanged?.Invoke(this, EventArgs.Empty);
                        }
                    }
                }
                finally
                {
                    foreach( var udpClient in udpClients )
                    {
                        udpClient?.Close();
                    }
                }


                // Will throw in case of receiver error.
                await receiverTask.ContextFree();

                return this.DiscoveredServices;
            }
            finally
            {
                this.findingServices = false;
                this.syncContext = null;
                this.clientId = Guid.Empty;
            }

            async Task SendRequestAsync(UdpClient udpClient, IPEndPoint discoveryEp, byte[] requestData)
            {
                try
                {
                    int nBytesSent = await udpClient.SendAsync(requestData, requestData.Length, discoveryEp).ContextFree();
                    if (nBytesSent < requestData.Length)
                    {
                        this.logger?.LogWarning("Failed to send full discovery request (request size: {RequestSize}, bytes sent: {BytesSent}", requestData.Length, nBytesSent);
                    }
                }
                catch (SocketException x)
                {
                    // E.g. the interface went down. The other interfaces can still be used, and the interface may be
                    // usable again by the next request.
                    this.logger?.LogInformation(x, "Failed to send discovery request to {DiscoveryEndPoint} from {LocalEndPoint}.", discoveryEp, udpClient.Client.LocalEndPoint);
                }
            }

        }


        private void HandlePublishedSingletons(LightweightRpcFrame responseFrame, IPEndPoint remoteEndPoint)
        {
            var response = ServiceDiscoveryOperations.DiscoverySerializer.Deserialize<RpcPublishedSingletonsResponse>(responseFrame.Payload);
            if (response != null && response.ClientId == this.clientId && response.ConnectionInfo != null)
            {
                bool isNewServer = false;
                bool changed = false;

                DiscoveredServer? discoveredServer;
                ImmutableArray<DiscoveredService> oldServices = ImmutableArray<DiscoveredService>.Empty;
                lock (this.syncRoot)
                {
                    if (!this.discoveredServers.TryGetValue(response.ConnectionInfo.ServerId, out discoveredServer))
                    {
                        // Let's create a new connection URI. The one received from the server
                        // includes the host or IP that the server has suggested, but the remoteEndPoint
                        // contains the actual IP used to reach the server.

                        // Only a TCP end point can be reached at the remote address. Other end points, e.g. named pipes
                        // of a local server, are discovered as suggested by the server. An IPv6 link-local address is
                        // only valid together with a zone index, which doesn't work well in a URL, so then the
                        // suggested host is used as well.
                        RpcConnectionInfo connectionInfo = response.ConnectionInfo;
                        if (response.ConnectionInfo.HostUrl is Uri uri && uri.Scheme == WellKnownRpcSchemes.LightweightTcp && uri.Port >= 0
                            && !remoteEndPoint.Address.IsIPv6LinkLocal)
                        {
                            connectionInfo = TcpRpcConnection.CreateConnectionInfo(connectionInfo.DisplayName, new IPEndPoint(remoteEndPoint.Address, uri.Port), connectionInfo.ServerId);
                        }

                        discoveredServer = new DiscoveredServer(connectionInfo);
                        this.discoveredServers.Add(connectionInfo.ServerId, discoveredServer);
                        changed = isNewServer = true;
                    }

                    oldServices = discoveredServer.Services;

                    if (responseFrame.MessageNumber > discoveredServer.RequestNoLastFound)
                    {
                        if (discoveredServer.UpdateServicesLocked(response.Services, responseFrame.MessageNumber))
                        {
                            changed = true;
                        }
                    }

                    if( changed)
                    {
                        this.discoveredServices = null;
                    }
                }

                if (isNewServer)
                {
                    this.RaiseEvent(this.ServerDiscovered, new DiscoveredServerEventArgs(discoveredServer.ConnectionInfo));
                }

                this.UpdateDiscoveredServices(discoveredServer.ConnectionInfo, discoveredServer.Services, oldServices);

                if( changed )
                {
                    this.RaiseEvent(this.ServicesChanged, EventArgs.Empty);
                }
            }            
        }

        private void UpdateDiscoveredServices(RpcConnectionInfo connectionInfo, ImmutableArray<DiscoveredService>? services, ImmutableArray<DiscoveredService> oldServices)
        {
            if (services != null)
            {
                foreach (var service in services)
                {
                    if (!string.IsNullOrEmpty(service?.Service))
                    {
                        var oldService = oldServices.FirstOrDefault(s => s.Service == service!.Service);
                        if (oldService == null)
                        {
                            // It's a new one.
                            this.RaiseEvent(this.ServiceDiscovered, new DiscoveredServiceEventArgs(new DiscoveredService(connectionInfo, service!.Service)));
                        }
                    }
                }
            }

            if (oldServices != null)
            {
                foreach (var service in oldServices)
                {
                    if (!string.IsNullOrEmpty(service?.Service))
                    {
                        var newService = services?.FirstOrDefault(s => s.Service == service!.Service);
                        if (newService == null)
                        {
                            // It's a lost one.
                            this.RaiseEvent(this.ServiceLost, new DiscoveredServiceEventArgs(new DiscoveredService(connectionInfo, service!.Service)));
                        }
                    }
                }
            }
        }

        private void RaiseEvent<TEventArgs>(EventHandler<TEventArgs>? eventHandler, TEventArgs eventArgs)
        {
            if (eventHandler != null)
            {
                if (this.syncContext is SynchronizationContext syncContext)
                {
                    syncContext.Post(_ => eventHandler(this, eventArgs), null);
                }
                else
                {
                    eventHandler(this, eventArgs);
                }
            }
        }
        private void RaiseEvent(EventHandler? eventHandler, EventArgs eventArgs)
        {
            if (eventHandler != null)
            {
                if (this.syncContext is SynchronizationContext syncContext)
                {
                    syncContext.Post(_ => eventHandler(this, eventArgs), null);
                }
                else
                {
                    eventHandler(this, eventArgs);
                }
            }
        }

        private async Task RunReceiver(IReadOnlyList<UdpClient> udpClients, CancellationToken cancellationToken)
        {
            using (this.logger?.BeginScope("LightweightDiscoveryClient.RunReceiver"))
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
#if NET
                                receiveTasks[index] = udpClients[index].ReceiveAsync(cancellationToken).AsTask();
#else
                                receiveTasks[index] = udpClients[index].ReceiveAsync();
#endif
                            }
                        }

                        var receiveTask = await Task.WhenAny(receiveTasks!).ContextFree();
                        int receiveIndex = Array.IndexOf(receiveTasks, receiveTask);
                        if (receiveIndex < 0) throw new InvalidOperationException("Unexpected receive task finished");

                        receiveTasks[receiveIndex] = null;
                        
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            var receiveResult = receiveTask.AwaiterResult();
                            try
                            {
                                if (LightweightRpcFrame.TryRead(receiveResult.Buffer, MaxDiscoveryFrameSize, out var responseFrame) == RpcFrameState.Full)
                                {
                                    switch (responseFrame.RpcOperation)
                                    {
                                        case ServiceDiscoveryOperations.GetPublishedSingletons:
                                            HandlePublishedSingletons(responseFrame, receiveResult.RemoteEndPoint);
                                            break;
                                        case ServiceDiscoveryOperations.GetConnectionInfo:
                                            // TODO: HandleConnections(clientId, syncContext, responseFrame);
                                            break;
                                    }
                                }
                            }
                            catch (Exception x) when (!x.IsFatal())
                            {
                                // Anyone on the network can send to the multicast group, so an invalid response must
                                // not end the discovery.
                                this.logger?.LogInformation(x, "Ignoring invalid discovery response from {RemoteEndPoint}.", receiveResult.RemoteEndPoint);
                            }
                        }
                    }
                    catch (Exception x)
                    {
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            this.logger?.LogWarning(x, "Error in LightweightDiscoveryClient.RunReceiver.");
                            throw;
                        }
                    }
                }
            }            
        }
        private sealed class DiscoveredServer
        {
            internal DiscoveredServer(RpcConnectionInfo connectionInfo)
            {
                this.ConnectionInfo = connectionInfo;
                this.timeLastUpdate = DateTime.UtcNow;
            }

            internal RpcConnectionInfo ConnectionInfo { get; }

            internal ImmutableArray<DiscoveredService> Services { get; private set; } = ImmutableArray<DiscoveredService>.Empty;

            internal int RequestNoLastFound { get; private set; }

            private DateTime timeLastUpdate;

            internal bool HasExpired(DateTime expiryTime)
                => this.timeLastUpdate < expiryTime;

            internal bool UpdateServicesLocked(RpcPublishedSingleton[]? newServices, int requestNo )
            {
                var builder = ImmutableArray.CreateBuilder<DiscoveredService>();
                bool changed = false;
                if (newServices != null)
                {
                    foreach (var newService in newServices)
                    {
                        if (!string.IsNullOrEmpty(newService?.Name))
                        {
                            var service = this.Services.FirstOrDefault(s => s.Service == newService!.Name);
                            if (service == null)
                            {
                                service = new DiscoveredService(this.ConnectionInfo, newService!.Name!);
                                changed = true;
                            } else
                            {
                                service.LastDiscoveryTime = DateTime.UtcNow;
                            }

                            builder.Add(service);
                        }
                    }
                }

                if( this.Services.Length != builder.Count )
                {
                    changed = true;
                }

                this.Services = builder.ToImmutable();
                this.RequestNoLastFound = requestNo;
                this.timeLastUpdate = DateTime.UtcNow;
                return changed;
            }
        }

    }
}
