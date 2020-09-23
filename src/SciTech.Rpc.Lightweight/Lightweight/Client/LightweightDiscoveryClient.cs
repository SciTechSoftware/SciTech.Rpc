using Microsoft.Extensions.Logging;
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
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Client
{
    public sealed class DiscoveredServerEventArgs : EventArgs
    {
        internal DiscoveredServerEventArgs(RpcServerConnectionInfo connectionInfo)
        {
            this.DiscoveredServer = connectionInfo;
        }

        public RpcServerConnectionInfo DiscoveredServer { get; }
    }

    public sealed class DiscoveredService
    {
        internal DiscoveredService(RpcServerConnectionInfo connectionInfo, string service/*, int version*/)
        {
            this.ConnectionInfo = connectionInfo;
            this.Service = service;
            //this.Version = version;
        }

        public RpcServerConnectionInfo ConnectionInfo { get; }

        public string Service { get; }

        // Not implemented yet.
        //public int Version { get; }
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

        private IImmutableList<DiscoveredService>? discoveredServices;

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
        // public event EventHandler<DiscoveredServerEventArgs>? ServerLost;

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
        public IImmutableList<DiscoveredService> DiscoveredServices
        {
            get
            {
                lock( this.syncRoot )
                {
                    if( this.discoveredServices == null )
                    {
                        var builder = ImmutableArray.CreateBuilder<DiscoveredService>();
                        builder.AddRange(discoveredServers.Values.SelectMany(s => s.Services));
                        this.discoveredServices = builder.ToImmutableArray();
                    }

                    return this.discoveredServices;
                }
            }
        }

        public async Task<IImmutableList<DiscoveredService>> FindServicesAsync(CancellationToken cancellationToken)
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

                using (var udpClient = new UdpClient())
                {
                    udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
                    udpClient.JoinMulticastGroup(LightweightDiscoveryEndPoint.DefaultMulticastAddress);

                    receiverTask = this.RunReceiver(udpClient, cancellationToken);

                    using var frameWriter = new LightweightRpcFrameWriter(MaxDiscoveryFrameSize);

                    IPEndPoint discoveryEp = new IPEndPoint(LightweightDiscoveryEndPoint.DefaultMulticastAddress, LightweightDiscoveryEndPoint.DefaultDiscoveryPort);
                    int nextRequestNo = 1;
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var requestHeader = new LightweightRpcFrame(
                            RpcFrameType.UnaryRequest, nextRequestNo++,
                            ServiceDiscoveryOperations.GetPublishedSingletons, null);

                        var requestData = frameWriter.WriteFrame(requestHeader, new RpcDiscoveryRequest(clientId), ServiceDiscoveryOperations.DiscoverySerializer);
                        int nBytesSent = await udpClient.SendAsync(requestData, requestData.Length, discoveryEp).ContextFree();
                        if (nBytesSent < requestData.Length)
                        {
                            // 
                            this.logger?.LogWarning("Failed to send full discovery request (request size: {RequestSize}, bytes sent: {BytesSent}", requestData.Length, nBytesSent);
                        }

                        Task finishedTask = await Task.WhenAny(Task.Delay(1000, cancellationToken), receiverTask).ContextFree();
                        if (finishedTask == receiverTask)
                        {
                            // Probably an error in the receiver. Stop Find.
                            break;
                        }

                        // TODO: Cleanup lost servers (e.g. with no response the last 10 requests).
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
        }

        private void HandlePublishedSingletons(LightweightRpcFrame responseFrame)
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
                        discoveredServer = new DiscoveredServer(response.ConnectionInfo);
                        this.discoveredServers.Add(response.ConnectionInfo.ServerId, discoveredServer);
                        changed = isNewServer = true;
                    }


                    if (responseFrame.MessageNumber > discoveredServer.RequestNoLastFound)
                    {
                        oldServices = discoveredServer.Services;
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

        private void UpdateDiscoveredServices(RpcServerConnectionInfo connectionInfo, ImmutableArray<DiscoveredService> services, ImmutableArray<DiscoveredService> oldServices)
        {
            foreach( var service in services )
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

            foreach (var service in oldServices)
            {
                if (!string.IsNullOrEmpty(service?.Service))
                {
                    var newService = services.FirstOrDefault(s => s.Service == service!.Service);
                    if (newService == null)
                    {
                        // It's a lost one.
                        this.RaiseEvent(this.ServiceLost, new DiscoveredServiceEventArgs(new DiscoveredService(connectionInfo, service!.Service)));
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

        private async Task RunReceiver(UdpClient udpClient, CancellationToken cancellationToken)
        {
            using (this.logger?.BeginScope("LightweightDiscoveryClient.RunReceiver"))
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var receiveResult = await udpClient.ReceiveAsync().ContextFree();
                        if( LightweightRpcFrame.TryRead( receiveResult.Buffer, MaxDiscoveryFrameSize, out var responseFrame) == RpcFrameState.Full )
                        {
                            switch( responseFrame.RpcOperation)
                            {
                                case ServiceDiscoveryOperations.GetPublishedSingletons:
                                    HandlePublishedSingletons(responseFrame);
                                    break;
                                case ServiceDiscoveryOperations.GetConnectionInfo:
                                    // TODO: HandleConnections(clientId, syncContext, responseFrame);
                                    break;
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
            internal DiscoveredServer(RpcServerConnectionInfo connectionInfo)
            {
                this.ConnectionInfo = connectionInfo;
            }

            internal RpcServerConnectionInfo ConnectionInfo { get; }

            internal ImmutableArray<DiscoveredService> Services { get; private set; } = ImmutableArray<DiscoveredService>.Empty;

            internal int RequestNoLastFound { get; private set; }

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

                return changed;
            }
        }

    }
}
