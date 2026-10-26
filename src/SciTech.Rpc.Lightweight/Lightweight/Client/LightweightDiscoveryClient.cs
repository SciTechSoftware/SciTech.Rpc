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
using SciTech.Collections.Immutable;
using SciTech.Diagnostics;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Lightweight.Server;
using SciTech.Rpc.Logging;
using SciTech.Rpc.Serialization;
using SciTech.Threading;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Client
{
    public sealed class DiscoveredServerEventArgs : EventArgs
    {
        internal DiscoveredServerEventArgs(DiscoveredServer discoveredServer)
        {
            this.DiscoveredServer = discoveredServer;
        }

        public DiscoveredServer DiscoveredServer { get; }
    }

    /// <summary>
    /// A server found by <see cref="LightweightDiscoveryClient"/>. Instances are immutable; when the information about
    /// the server changes, a new instance replaces this one.
    /// </summary>
    public sealed class DiscoveredServer
    {
        internal DiscoveredServer(
            RpcConnectionInfo connectionInfo,
            IReadOnlyDictionary<string, string> properties,
            IReadOnlyList<string> services,
            int serviceCount)
        {
            this.ConnectionInfo = connectionInfo;
            this.Properties = properties;
            this.Services = services;
            this.ServiceCount = serviceCount;
        }

        /// <summary>
        /// Gets the connection info to use to connect to the server.
        /// </summary>
        public RpcConnectionInfo ConnectionInfo { get; }

        /// <summary>
        /// Gets the server properties, as defined by the server application (see <c>RpcServerOptions.ServerProperties</c>).
        /// The dictionary is immutable.
        /// </summary>
        public IReadOnlyDictionary<string, string> Properties { get; }

        public RpcServerId ServerId => this.ConnectionInfo.ServerId;

        /// <summary>
        /// Gets the number of discoverable singleton services of the server.
        /// </summary>
        public int ServiceCount { get; }

        /// <summary>
        /// Gets the discovered singleton services of the server. If services were requested, these are the requested
        /// services that the server publishes. Otherwise they're all discoverable services of the server, once they've
        /// been retrieved. The list is immutable.
        /// </summary>
        public IReadOnlyList<string> Services { get; }

        public override string ToString() => this.ConnectionInfo.ToString();
    }

    public sealed class DiscoveredService
    {
        internal DiscoveredService(RpcConnectionInfo connectionInfo, string service)
        {
            this.ConnectionInfo = connectionInfo;
            this.Service = service;
        }

        public RpcConnectionInfo ConnectionInfo { get; }

        public string Service { get; }
    }

    public sealed class DiscoveredServiceEventArgs : EventArgs
    {
        internal DiscoveredServiceEventArgs(DiscoveredService discoveredService)
        {
            this.DiscoveredService = discoveredService;
        }

        public DiscoveredService DiscoveredService { get; }
    }

    /// <summary>
    /// Finds Lightweight RPC servers on the local network, using the discovery protocol of the Lightweight protocol
    /// specification (docs/lightweight-protocol.md, section 7).
    /// </summary>
    public class LightweightDiscoveryClient
    {
        private readonly Dictionary<RpcServerId, ServerState> discoveredServers = new Dictionary<RpcServerId, ServerState>();

        private readonly ILogger? logger;

        private readonly object syncRoot = new object();

        private Guid clientId;

        private ImmutableArrayList<DiscoveredServer>? discoveredServersList;

        private ImmutableArrayList<DiscoveredService>? discoveredServicesList;

        private bool findingServices;

        private int nextRequestNo;

        private string[]? requestedServices;

        private SynchronizationContext? syncContext;

        public LightweightDiscoveryClient(ILogger? logger = null)
        {
            this.logger = logger ?? RpcLogger.TryCreateLogger<LightweightDiscoveryClient>();
        }

        /// <summary>
        /// Occurs when a new RPC server has been discovered.
        /// <note>Will be invoked in the synchronization context of the <see cref="FindServicesAsync(CancellationToken)"/> caller.</note>
        /// </summary>
        public event EventHandler<DiscoveredServerEventArgs>? ServerDiscovered;

        /// <summary>
        /// Occurs when the information about a discovered RPC server has changed, e.g. its properties or services.
        /// <see cref="DiscoveredServerEventArgs.DiscoveredServer"/> is the new information.
        /// <note>Will be invoked in the synchronization context of the <see cref="FindServicesAsync(CancellationToken)"/> caller.</note>
        /// </summary>
        public event EventHandler<DiscoveredServerEventArgs>? ServerChanged;

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

        /// <summary>
        /// Occurs when <see cref="DiscoveredServers"/> or <see cref="DiscoveredServices"/> have changed.
        /// <note>Will be invoked in the synchronization context of the <see cref="FindServicesAsync(CancellationToken)"/> caller.</note>
        /// </summary>
        public event EventHandler? ServicesChanged;

        /// <summary>
        /// Gets the RPC servers discovered during the current or last call to <see cref="FindServicesAsync(CancellationToken)"/>.
        /// The list is an immutable snapshot.
        /// </summary>
        public IReadOnlyList<DiscoveredServer> DiscoveredServers
        {
            get
            {
                lock (this.syncRoot)
                {
                    return this.discoveredServersList ??= this.discoveredServers.Values.Select(s => s.Server).ToImmutableArrayList();
                }
            }
        }

        /// <summary>
        /// Gets the RPC services discovered during the current or last call to <see cref="FindServicesAsync(CancellationToken)"/>.
        /// The list is an immutable snapshot.
        /// </summary>
        public IReadOnlyList<DiscoveredService> DiscoveredServices
        {
            get
            {
                lock (this.syncRoot)
                {
                    return this.discoveredServicesList ??= this.discoveredServers.Values
                        .SelectMany(s => s.Server.Services.Select(service => new DiscoveredService(s.Server.ConnectionInfo, service)))
                        .ToImmutableArrayList();
                }
            }
        }

        /// <summary>
        /// Gets or sets the interval between discovery requests. The default is 2 seconds.
        /// </summary>
        public TimeSpan RequestInterval { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Gets or sets the time without responses after which a server is considered lost. It should span several
        /// <see cref="RequestInterval"/>s, so that a single lost datagram doesn't make a server come and go. The default
        /// is 10 seconds.
        /// </summary>
        public TimeSpan ServerLostTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gets or sets the address families that discovery requests are sent on. <c>null</c> (the default) means
        /// both IPv4 and IPv6. Internal to allow testing a single address family.
        /// </summary>
        internal IReadOnlyCollection<AddressFamily>? AddressFamilies { get; set; }

        /// <summary>
        /// Finds servers and all their discoverable services, until <paramref name="cancellationToken"/> is cancelled.
        /// </summary>
        /// <returns>An immutable snapshot of the services discovered when the discovery was cancelled.</returns>
        public Task<IReadOnlyList<DiscoveredService>> FindServicesAsync(CancellationToken cancellationToken)
            => this.FindServicesAsync(null, cancellationToken);

        /// <summary>
        /// Finds servers, until <paramref name="cancellationToken"/> is cancelled. If <paramref name="services"/> is
        /// provided, only servers that publish at least one of them are discovered, and <see cref="DiscoveredServer.Services"/>
        /// lists which. E.g. a client can request both the original and a newer version of a service, and show the servers
        /// that only publish the original service as incompatible. Servers that publish none of the services, i.e. other
        /// kinds of servers, don't respond.
        /// </summary>
        /// <param name="services">The names of the singleton services to discover, or <c>null</c> to discover all
        /// discoverable services. At most 32 names, and they must fit in a single discovery datagram.</param>
        /// <returns>An immutable snapshot of the services discovered when the discovery was cancelled.</returns>
        public async Task<IReadOnlyList<DiscoveredService>> FindServicesAsync(IEnumerable<string>? services, CancellationToken cancellationToken)
        {
            var requestedServices = services?.ToArray();
            ValidateRequestedServices(requestedServices);

            lock (this.syncRoot)
            {
                if (this.findingServices)
                {
                    // To simplify things a bit.
                    throw new InvalidOperationException($"Only a single {nameof(FindServicesAsync)} call can be running at the time.");
                }

                this.findingServices = true;
                this.syncContext = SynchronizationContext.Current;
                this.clientId = Guid.NewGuid();
                this.requestedServices = requestedServices;

                // Each call starts over, without events for the servers of the previous call.
                this.discoveredServers.Clear();
                this.discoveredServersList = null;
                this.discoveredServicesList = null;
            }

            try
            {
                var discoverySockets = this.CreateSockets();
                if (discoverySockets.Count == 0)
                {
                    // TODO: Use NetworkChange.NetworkAvailabilityChanged to update the sockets until cancelled.
                    return ImmutableArrayList<DiscoveredService>.Empty;
                }

                Task receiverTask;
                try
                {
                    receiverTask = this.RunReceiver(discoverySockets, cancellationToken);

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var requestData = this.CreateFindServersRequest();
                        foreach (var socket in discoverySockets)
                        {
                            await this.SendAsync(socket.UdpClient, socket.DiscoveryEndPoint, requestData).ContextFree();
                            if (cancellationToken.IsCancellationRequested) break;
                        }

                        var finishedTask = await Task.WhenAny(Task.Delay(this.RequestInterval, cancellationToken), receiverTask).ContextFree();
                        if (finishedTask == receiverTask)
                        {
                            // Probably an error in the receiver. Stop Find.
                            break;
                        }

                        this.RemoveLostServers();
                        await this.RetryServiceRequestsAsync().ContextFree();
                    }
                }
                finally
                {
                    foreach (var socket in discoverySockets)
                    {
                        socket.UdpClient.Close();
                    }
                }

                // Will throw in case of receiver error.
                await receiverTask.ContextFree();

                return this.DiscoveredServices;
            }
            finally
            {
                lock (this.syncRoot)
                {
                    this.findingServices = false;
                    this.syncContext = null;
                    this.clientId = Guid.Empty;
                }
            }
        }

        private static bool AreEqual(IReadOnlyDictionary<string, string> properties1, IReadOnlyDictionary<string, string> properties2)
        {
            if (properties1.Count != properties2.Count)
            {
                return false;
            }

            foreach (var pair in properties1)
            {
                if (!properties2.TryGetValue(pair.Key, out var value) || value != pair.Value)
                {
                    return false;
                }
            }

            return true;
        }

        private static byte[] CreateRequest<TRequest>(string operation, int requestNo, TRequest request)
            where TRequest : class
        {
            using var frameWriter = new LightweightRpcFrameWriter(ServiceDiscoveryOperations.MaxDatagramSize);
            var requestHeader = new LightweightRpcFrame(RpcFrameType.UnaryRequest, requestNo, operation, null);
            return ServiceDiscoveryOperations.PadRequest(frameWriter.WriteFrame(requestHeader, request, ServiceDiscoveryOperations.DiscoverySerializer));
        }

        private static void ValidateRequestedServices(string[]? services)
        {
            if (services != null)
            {
                if (services.Any(string.IsNullOrEmpty))
                {
                    throw new ArgumentException("Service names must not be null or empty.", nameof(services));
                }

                if (services.Length > ServiceDiscoveryOperations.MaxRequestedServices)
                {
                    throw new ArgumentException($"At most {ServiceDiscoveryOperations.MaxRequestedServices} services can be discovered.", nameof(services));
                }

                var requestData = CreateRequest(ServiceDiscoveryOperations.FindServers, int.MaxValue, new RpcFindServersRequest(Guid.Empty, services));
                if (requestData.Length > ServiceDiscoveryOperations.MaxDatagramSize)
                {
                    throw new ArgumentException("The service names don't fit in a discovery request.", nameof(services));
                }
            }
        }

        /// <summary>
        /// Creates the connection info of a newly discovered server, and indicates whether it is derived from the address
        /// of the server.
        /// </summary>
        private static (RpcConnectionInfo ConnectionInfo, bool IsAddressDerived) CreateConnectionInfo(RpcConnectionInfo suggestedInfo, IPEndPoint remoteEndPoint)
        {
            // The connection URL received from the server includes the host or IP that the server has suggested, but the
            // remote end point contains the actual IP used to reach the server.
            // Only a TCP end point can be reached at the remote address. Other end points, e.g. named pipes of a local
            // server, are discovered as suggested by the server. An IPv6 link-local address is only valid together with a
            // zone index, which doesn't work well in a URL, so then the suggested host is used as well.
            if (suggestedInfo.HostUrl is Uri uri && uri.Scheme == WellKnownRpcSchemes.LightweightTcp && uri.Port >= 0
                && !remoteEndPoint.Address.IsIPv6LinkLocal)
            {
                return (TcpRpcConnection.CreateConnectionInfo(suggestedInfo.DisplayName, new IPEndPoint(remoteEndPoint.Address, uri.Port), suggestedInfo.ServerId), true);
            }

            return (suggestedInfo, false);
        }

        private byte[] CreateFindServersRequest()
        {
            Guid clientId;
            string[]? requestedServices;
            lock (this.syncRoot)
            {
                clientId = this.clientId;
                requestedServices = this.requestedServices;
            }

            int requestNo = Interlocked.Increment(ref this.nextRequestNo);
            return CreateRequest(ServiceDiscoveryOperations.FindServers, requestNo, new RpcFindServersRequest(clientId, requestedServices));
        }

        private List<DiscoverySocket> CreateSockets()
        {
            var sockets = new List<DiscoverySocket>();
            foreach (var (networkInterface, ipAddress) in LightweightDiscoveryEndPoint.GetDiscoveryInterfaces(this.AddressFamilies))
            {
                var udpClient = LightweightDiscoveryEndPoint.CreateDiscoveryClient(ipAddress);
                try
                {
                    // Responses are sent to the bound address. The client doesn't need to join the discovery group,
                    // it only sends to it.
                    udpClient.Client.Bind(new IPEndPoint(ipAddress, 0));
                    LightweightDiscoveryEndPoint.SetMulticastInterface(udpClient, networkInterface, ipAddress);

                    sockets.Add(new DiscoverySocket(udpClient,
                        new IPEndPoint(LightweightDiscoveryEndPoint.GetDiscoveryGroup(ipAddress), LightweightDiscoveryEndPoint.DefaultDiscoveryPort)));
                }
                catch (SocketException x)
                {
                    this.logger?.LogInformation(x, "Failed to send discovery requests on network interface {NetworkInterface} ({Address}).", networkInterface.Name, ipAddress);
                    udpClient.Dispose();
                }
            }

            return sockets;
        }

        private void HandleFindServersResponse(LightweightRpcFrame responseFrame, DiscoverySocket socket, IPEndPoint remoteEndPoint, List<Action> events, List<PendingRequest> requests)
        {
            var response = ServiceDiscoveryOperations.DiscoverySerializer.Deserialize<RpcFindServersResponse>(responseFrame.Payload);
            if (response?.ConnectionInfo == null || response.ConnectionInfo.ServerId == RpcServerId.Empty)
            {
                return;
            }

            lock (this.syncRoot)
            {
                if (response.ClientId != this.clientId || !this.findingServices)
                {
                    return;
                }

                var serverId = response.ConnectionInfo.ServerId;
                bool isNewServer = !this.discoveredServers.TryGetValue(serverId, out var state);
                if (state == null)
                {
                    var (initialConnectionInfo, isAddressDerived) = CreateConnectionInfo(response.ConnectionInfo, remoteEndPoint);
                    state = new ServerState(new DiscoveredServer(initialConnectionInfo, FrozenDictionary<string, string>.Empty, ImmutableArrayList<string>.Empty, 0), isAddressDerived);
                    this.discoveredServers.Add(serverId, state);
                }

                state.LastResponseTime = DateTime.UtcNow;

                // Responses to an older request may arrive late, so they must not replace newer information.
                if (responseFrame.MessageNumber < state.LastRequestNo)
                {
                    return;
                }

                state.LastRequestNo = responseFrame.MessageNumber;

                var oldServer = state.Server;
                var connectionInfo = oldServer.ConnectionInfo;
                if (!state.IsAddressDerived)
                {
                    // A connection derived from an address is preferred, e.g. if the server was first found through an
                    // IPv6 link-local address and then through an IPv4 address.
                    var (newConnectionInfo, isAddressDerived) = CreateConnectionInfo(response.ConnectionInfo, remoteEndPoint);
                    if (isAddressDerived)
                    {
                        connectionInfo = newConnectionInfo;
                        state.IsAddressDerived = true;
                    }
                }

                // The properties rarely change, so they're only frozen when they have.
                var properties = oldServer.Properties;
                var responseProperties = (IReadOnlyDictionary<string, string>?)response.Properties ?? FrozenDictionary<string, string>.Empty;
                if (!AreEqual(responseProperties, properties))
                {
                    properties = responseProperties.ToFrozenDictionary(StringComparer.Ordinal);
                }

                var services = oldServer.Services;
                if (this.requestedServices is string[] requestedServices)
                {
                    var matchedServices = (response.Matches ?? Array.Empty<int>())
                        .Where(i => i >= 0 && i < requestedServices.Length)
                        .Distinct()
                        .OrderBy(i => i)
                        .Select(i => requestedServices[i])
                        .ToImmutableArrayList();
                    if (!matchedServices.SequenceEqual(services))
                    {
                        services = matchedServices;
                    }
                }
                else if (response.ServicesHash != state.ServicesHash)
                {
                    if (response.ServiceCount <= 0)
                    {
                        services = ImmutableArrayList<string>.Empty;
                        state.ServicesHash = response.ServicesHash;
                        state.ServicesRequest = null;
                    }
                    else if (state.ServicesRequest?.ServicesHash != response.ServicesHash)
                    {
                        // The services have changed, or haven't been retrieved yet. They're retrieved one page at a time,
                        // through the socket that the server responded on.
                        state.ServicesRequest = new ServicesRequest(response.ServicesHash, serverId, socket);
                        requests.Add(this.CreateServicesRequestLocked(state.ServicesRequest));
                    }
                }

                if (isNewServer || connectionInfo != oldServer.ConnectionInfo || properties != oldServer.Properties
                    || services != oldServer.Services || response.ServiceCount != oldServer.ServiceCount)
                {
                    state.Server = new DiscoveredServer(connectionInfo, properties, services, response.ServiceCount);
                    this.AddServerEventsLocked(oldServer, state.Server, isNewServer, events);
                }
            }
        }

        private void HandleGetServicesResponse(LightweightRpcFrame responseFrame, List<Action> events, List<PendingRequest> requests)
        {
            var response = ServiceDiscoveryOperations.DiscoverySerializer.Deserialize<RpcGetServicesResponse>(responseFrame.Payload);
            if (response == null)
            {
                return;
            }

            lock (this.syncRoot)
            {
                if (response.ClientId != this.clientId || !this.findingServices
                    || !this.discoveredServers.TryGetValue(new RpcServerId(response.ServerId), out var state)
                    || state.ServicesRequest is not ServicesRequest servicesRequest)
                {
                    return;
                }

                state.LastResponseTime = DateTime.UtcNow;

                if (response.ServicesHash != servicesRequest.ServicesHash)
                {
                    // The services changed between pages, so start over.
                    state.ServicesRequest = servicesRequest = new ServicesRequest(response.ServicesHash, servicesRequest.ServerId, servicesRequest.Socket);
                    requests.Add(this.CreateServicesRequestLocked(servicesRequest));
                    return;
                }

                if (response.Offset != servicesRequest.Services.Count)
                {
                    // A duplicate or late response.
                    return;
                }

                var pageServices = response.Services ?? Array.Empty<string>();
                servicesRequest.Services.AddRange(pageServices.Where(s => !string.IsNullOrEmpty(s)));

                if (servicesRequest.Services.Count < response.ServiceCount && pageServices.Length > 0)
                {
                    requests.Add(this.CreateServicesRequestLocked(servicesRequest));
                    return;
                }

                state.ServicesHash = servicesRequest.ServicesHash;
                state.ServicesRequest = null;

                var oldServer = state.Server;
                var services = servicesRequest.Services.ToImmutableArrayList();
                if (!services.SequenceEqual(oldServer.Services) || response.ServiceCount != oldServer.ServiceCount)
                {
                    state.Server = new DiscoveredServer(oldServer.ConnectionInfo, oldServer.Properties, services, response.ServiceCount);
                    this.AddServerEventsLocked(oldServer, state.Server, false, events);
                }
            }
        }

        /// <summary>
        /// Adds the events for the change from <paramref name="oldServer"/> to <paramref name="newServer"/> to <paramref name="events"/>.
        /// The events are raised once the lock has been released.
        /// </summary>
        private void AddServerEventsLocked(DiscoveredServer? oldServer, DiscoveredServer? newServer, bool isNewServer, List<Action> events)
        {
            this.discoveredServersList = null;
            this.discoveredServicesList = null;

            var server = newServer ?? oldServer!;
            var eventArgs = new DiscoveredServerEventArgs(server);
            if (isNewServer)
            {
                events.Add(() => this.RaiseEvent(this.ServerDiscovered, eventArgs));
            }
            else if (newServer == null)
            {
                events.Add(() => this.RaiseEvent(this.ServerLost, eventArgs));
            }
            else
            {
                events.Add(() => this.RaiseEvent(this.ServerChanged, eventArgs));
            }

            var oldServices = (isNewServer ? null : oldServer?.Services) ?? ImmutableArrayList<string>.Empty;
            var newServices = newServer?.Services ?? ImmutableArrayList<string>.Empty;
            bool connectionChanged = !isNewServer && newServer != null && oldServer != null && newServer.ConnectionInfo != oldServer.ConnectionInfo;

            foreach (var service in oldServices)
            {
                if (connectionChanged || !newServices.Contains(service))
                {
                    var serviceEventArgs = new DiscoveredServiceEventArgs(new DiscoveredService(oldServer!.ConnectionInfo, service));
                    events.Add(() => this.RaiseEvent(this.ServiceLost, serviceEventArgs));
                }
            }

            foreach (var service in newServices)
            {
                if (connectionChanged || !oldServices.Contains(service))
                {
                    var serviceEventArgs = new DiscoveredServiceEventArgs(new DiscoveredService(newServer!.ConnectionInfo, service));
                    events.Add(() => this.RaiseEvent(this.ServiceDiscovered, serviceEventArgs));
                }
            }

            events.Add(() => this.RaiseEvent(this.ServicesChanged, EventArgs.Empty));
        }

        private PendingRequest CreateServicesRequestLocked(ServicesRequest servicesRequest)
        {
            servicesRequest.LastRequestTime = DateTime.UtcNow;
            int requestNo = Interlocked.Increment(ref this.nextRequestNo);
            var requestData = CreateRequest(ServiceDiscoveryOperations.GetServices, requestNo,
                new RpcGetServicesRequest(this.clientId, servicesRequest.ServerId.ToGuid(), servicesRequest.Services.Count));

            // The request is multicast, since servers on the same host share the discovery port, and a unicast datagram
            // would only reach one of them. Only the requested server answers.
            return new PendingRequest(servicesRequest.Socket.UdpClient, servicesRequest.Socket.DiscoveryEndPoint, requestData);
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

        private void RemoveLostServers()
        {
            var events = new List<Action>();
            lock (this.syncRoot)
            {
                var expiryTime = DateTime.UtcNow - this.ServerLostTimeout;
                foreach (var state in this.discoveredServers.Values.Where(s => s.LastResponseTime < expiryTime).ToList())
                {
                    this.discoveredServers.Remove(state.Server.ServerId);
                    this.AddServerEventsLocked(state.Server, null, false, events);
                }
            }

            foreach (var raiseEvent in events)
            {
                raiseEvent();
            }
        }

        /// <summary>
        /// Resends the service requests that haven't been answered within <see cref="RequestInterval"/>.
        /// </summary>
        private async Task RetryServiceRequestsAsync()
        {
            var requests = new List<PendingRequest>();
            lock (this.syncRoot)
            {
                var retryTime = DateTime.UtcNow - this.RequestInterval;
                foreach (var state in this.discoveredServers.Values)
                {
                    if (state.ServicesRequest is ServicesRequest servicesRequest && servicesRequest.LastRequestTime < retryTime)
                    {
                        requests.Add(this.CreateServicesRequestLocked(servicesRequest));
                    }
                }
            }

            foreach (var request in requests)
            {
                await this.SendAsync(request.UdpClient, request.RemoteEndPoint, request.RequestData).ContextFree();
            }
        }

        private async Task RunReceiver(IReadOnlyList<DiscoverySocket> sockets, CancellationToken cancellationToken)
        {
            using (this.logger?.BeginScope("LightweightDiscoveryClient.RunReceiver"))
            {
                Task<UdpReceiveResult>?[] receiveTasks = new Task<UdpReceiveResult>[sockets.Count];

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        for (int index = 0; index < receiveTasks.Length; index++)
                        {
                            if (receiveTasks[index] == null)
                            {
#if NET
                                receiveTasks[index] = sockets[index].UdpClient.ReceiveAsync(cancellationToken).AsTask();
#else
                                receiveTasks[index] = sockets[index].UdpClient.ReceiveAsync();
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
                            var events = new List<Action>();
                            var requests = new List<PendingRequest>();
                            try
                            {
                                if (LightweightRpcFrame.TryRead(receiveResult.Buffer, ServiceDiscoveryOperations.MaxDatagramSize, out var responseFrame) == RpcFrameState.Full
                                    && responseFrame.FrameType == RpcFrameType.UnaryResponse)
                                {
                                    switch (responseFrame.RpcOperation)
                                    {
                                        case ServiceDiscoveryOperations.FindServers:
                                            this.HandleFindServersResponse(responseFrame, sockets[receiveIndex], receiveResult.RemoteEndPoint, events, requests);
                                            break;
                                        case ServiceDiscoveryOperations.GetServices:
                                            this.HandleGetServicesResponse(responseFrame, events, requests);
                                            break;
                                    }
                                }
                            }
                            catch (Exception x) when (!x.IsFatal())
                            {
                                // Anyone on the network can send to the client, so an invalid response must not end the
                                // discovery.
                                this.logger?.LogInformation(x, "Ignoring invalid discovery response from {RemoteEndPoint}.", receiveResult.RemoteEndPoint);
                            }

                            foreach (var raiseEvent in events)
                            {
                                raiseEvent();
                            }

                            foreach (var request in requests)
                            {
                                await this.SendAsync(request.UdpClient, request.RemoteEndPoint, request.RequestData).ContextFree();
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

        private async Task SendAsync(UdpClient udpClient, IPEndPoint endPoint, byte[] requestData)
        {
            try
            {
                int nBytesSent = await udpClient.SendAsync(requestData, requestData.Length, endPoint).ContextFree();
                if (nBytesSent < requestData.Length)
                {
                    this.logger?.LogWarning("Failed to send full discovery request (request size: {RequestSize}, bytes sent: {BytesSent}", requestData.Length, nBytesSent);
                }
            }
            catch (SocketException x)
            {
                // E.g. the interface went down. The other interfaces can still be used, and the interface may be
                // usable again by the next request.
                this.logger?.LogInformation(x, "Failed to send discovery request to {EndPoint} from {LocalEndPoint}.", endPoint, udpClient.Client.LocalEndPoint);
            }
            catch (ObjectDisposedException)
            {
                // The discovery has ended.
            }
        }

        private sealed class DiscoverySocket
        {
            internal DiscoverySocket(UdpClient udpClient, IPEndPoint discoveryEndPoint)
            {
                this.UdpClient = udpClient;
                this.DiscoveryEndPoint = discoveryEndPoint;
            }

            internal IPEndPoint DiscoveryEndPoint { get; }

            internal UdpClient UdpClient { get; }
        }

        private sealed class PendingRequest
        {
            internal PendingRequest(UdpClient udpClient, IPEndPoint remoteEndPoint, byte[] requestData)
            {
                this.UdpClient = udpClient;
                this.RemoteEndPoint = remoteEndPoint;
                this.RequestData = requestData;
            }

            internal IPEndPoint RemoteEndPoint { get; }

            internal byte[] RequestData { get; }

            internal UdpClient UdpClient { get; }
        }

        private sealed class ServerState
        {
            internal ServerState(DiscoveredServer server, bool isAddressDerived)
            {
                this.Server = server;
                this.IsAddressDerived = isAddressDerived;
            }

            internal bool IsAddressDerived { get; set; }

            internal DateTime LastResponseTime { get; set; }

            internal int LastRequestNo { get; set; }

            internal DiscoveredServer Server { get; set; }

            /// <summary>
            /// The hash of the services in <see cref="Server"/>, when all services are discovered.
            /// </summary>
            internal string? ServicesHash { get; set; }

            internal ServicesRequest? ServicesRequest { get; set; }
        }

        /// <summary>
        /// The state of retrieving all services of a server.
        /// </summary>
        private sealed class ServicesRequest
        {
            internal ServicesRequest(string? servicesHash, RpcServerId serverId, DiscoverySocket socket)
            {
                this.ServicesHash = servicesHash;
                this.ServerId = serverId;
                this.Socket = socket;
            }

            internal DateTime LastRequestTime { get; set; }

            internal RpcServerId ServerId { get; }

            internal List<string> Services { get; } = new List<string>();

            internal string? ServicesHash { get; }

            /// <summary>
            /// The socket that the server responded on, i.e. the interface and address family that reach the server.
            /// </summary>
            internal DiscoverySocket Socket { get; }
        }
    }
}
