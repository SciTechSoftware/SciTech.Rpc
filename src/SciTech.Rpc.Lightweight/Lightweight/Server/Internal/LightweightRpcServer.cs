#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Lightweight.Server.Internal;
using SciTech.Rpc.Logging;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using SciTech.Threading;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server
{

    public partial class LightweightRpcServer : RpcServerHostBase
    {
        public const int DefaultMaxRequestMessageSize = 4 * 1024 * 1024;

        public const int DefaultMaxResponseMessageSize = 4 * 1024 * 1024;

        private static readonly MethodInfo CreateServiceStubBuilderMethod = typeof(LightweightRpcServer)
            .GetMethod(nameof(CreateServiceStubBuilder), BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new NotImplementedException($"Method {nameof(CreateServiceStubBuilder)} not found on type '{typeof(LightweightRpcServer)}'.");

        private static ILog Logger = LogProvider.For<LightweightRpcServer>();

        private readonly ConcurrentDictionary<ClientPipeline, ClientPipeline> clients
            = new ConcurrentDictionary<ClientPipeline, ClientPipeline>();

        private readonly Dictionary<string, LightweightMethodStub> methodDefinitions
            = new Dictionary<string, LightweightMethodStub>();

        private List<LightweightDiscoveryEndPoint> discoveryEndPoints = new List<LightweightDiscoveryEndPoint>();
        private List<LightweightRpcEndPoint> endPoints = new List<LightweightRpcEndPoint>();
        private List<ILightweightRpcListener> startedEndpoints = new List<ILightweightRpcListener>();

        public LightweightRpcServer(
            IServiceProvider? serviceProvider = null,
            RpcServerOptions? options = null,
            LightweightOptions? lightweightOptions = null)
            : this(RpcServerId.NewId(), null, serviceProvider, options, lightweightOptions)
        {
        }

        public LightweightRpcServer(
            IRpcServiceDefinitionsProvider? definitionsProvider,
            IServiceProvider? serviceProvider = null,
            RpcServerOptions? options = null,
            LightweightOptions? lightweightOptions = null)
            : this(RpcServerId.NewId(), definitionsProvider, serviceProvider, options, lightweightOptions)
        {
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="servicePublisher"></param>
        public LightweightRpcServer(
            RpcServicePublisher servicePublisher,
            IServiceProvider? serviceProvider = null,
            RpcServerOptions? options = null,
            LightweightOptions? lightweightOptions = null)
            : this(servicePublisher ?? throw new ArgumentNullException(nameof(servicePublisher)),
                  servicePublisher,
                  servicePublisher.DefinitionsProvider, serviceProvider, options, lightweightOptions)
        {
        }

        public LightweightRpcServer(
            RpcServerId serverId,
            IRpcServiceDefinitionsProvider? definitionsProvider = null,
            IServiceProvider? serviceProvider = null,
            RpcServerOptions? options = null,
            LightweightOptions? lightweightOptions = null)
            : this(new RpcServicePublisher(definitionsProvider ?? new RpcServiceDefinitionsBuilder(), serverId),
                  serviceProvider, options, lightweightOptions)
        {
        }

        /// <summary>
        /// Internal to allow testing.
        /// </summary>
        internal LightweightRpcServer(
            IRpcServicePublisher servicePublisher,
            IRpcServiceActivator serviceImplProvider,
            IRpcServiceDefinitionsProvider definitionsProvider,
            IServiceProvider? serviceProvider,
            RpcServerOptions? options,
            LightweightOptions? lightweightOptions = null)
            : base(servicePublisher, serviceImplProvider, definitionsProvider, options)
        {
            this.ServiceProvider = serviceProvider;
            this.MaxRequestSize = options?.ReceiveMaxMessageSize ?? DefaultMaxRequestMessageSize;
            this.MaxResponseSize = options?.SendMaxMessageSize ?? DefaultMaxResponseMessageSize;

            this.KeepSizeLimitedConnectionAlive = lightweightOptions?.KeepSizeLimitedConnectionAlive ?? true;
        }

        public int ClientCount => this.clients.Count;

        public bool KeepSizeLimitedConnectionAlive { get; }

        public int MaxRequestSize { get; }

        public int MaxResponseSize { get; }

        protected override IServiceProvider? ServiceProvider { get; }

        public void AddEndPoint(LightweightRpcEndPoint endPoint)
        {
            if (endPoint == null) throw new ArgumentNullException(nameof(endPoint));

            lock (this.SyncRoot)
            {
                this.CheckIsInitializing();
                this.endPoints.Add(endPoint);
            }

            this.ServicePublisher.TryInitConnectionInfo(endPoint.GetConnectionInfo(this.ServicePublisher.ServerId));
        }

        public void AddEndPoint(LightweightDiscoveryEndPoint endPoint)
        {
            if (endPoint == null) throw new ArgumentNullException(nameof(endPoint));

            lock (this.SyncRoot)
            {
                this.CheckIsInitializing();
                this.discoveryEndPoints.Add(endPoint);
            }
        }

        public override void AddEndPoint(IRpcServerEndPoint endPoint)
        {
            if (endPoint is LightweightRpcEndPoint lightweightEndPoint)
            {
                this.AddEndPoint(lightweightEndPoint);
            }
            else if (endPoint is LightweightDiscoveryEndPoint discoveryEndPoint)
            {

            }
            {
                throw new ArgumentException($"End point must implement {nameof(LightweightRpcEndPoint)} or {nameof(LightweightDiscoveryEndPoint)}.");
            }
        }

        internal LightweightMethodStub? GetMethodDefinition(string rpcOperation)
        {
            this.methodDefinitions.TryGetValue(rpcOperation, out var methodStub);
            return methodStub;
        }

        protected override void BuildServiceStub(Type serviceType)
        {
            var typedMethod = CreateServiceStubBuilderMethod.MakeGenericMethod(serviceType);
            var stubBuilder = (ILightweightServiceStubBuilder)typedMethod.Invoke(this, null)!;
            var serviceDef = stubBuilder.Build(this);

            this.AddServiceDef(serviceDef);
        }

        protected override void BuildServiceStubs()
        {
            var queryServicesStub = new LightweightMethodStub<RpcObjectRequest, RpcServicesQueryResponse>(
                "SciTech.Rpc.RpcService.QueryServices",
                (request, _, context) => new ValueTask<RpcServicesQueryResponse>(this.QueryServices(request.Id)),
                this.Serializer, null, false);
            this.AddMethodDef(queryServicesStub);

            if (this.AllowDiscovery)
            {
                var discovery = new ServiceDiscovery(this, this.CreateDefaultSerializer());
                discovery.AddDiscoveryMethods(new MethodBinder(this));
            }

            base.BuildServiceStubs();
        }

        protected override IRpcSerializer CreateDefaultSerializer()
        {
            return new JsonRpcSerializer();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cleanup")]
        protected override void Dispose(bool disposing)
        {
            if (!this.IsDisposed)
            {
                foreach (var client in this.clients)
                {
                    try { client.Key.Dispose(); } catch (Exception x) { Logger.Warn(x, "Error when disposing client."); }
                }
                this.clients.Clear();

                //foreach (var endPoint in endPoints)
                //{
                //    endPoint.Stop();
                //}
            }

            base.Dispose(disposing);
        }

        protected virtual ValueTask OnReceiveAsync(IMemoryOwner<byte> message) => default;

        protected async Task RunClientAsync(IDuplexPipe pipe, LightweightRpcEndPoint endPoint, CancellationToken cancellationToken = default)
        {
            using (var client = new ClientPipeline(pipe, this, endPoint, this.MaxRequestSize, this.MaxResponseSize, this.KeepSizeLimitedConnectionAlive))
            {
                try
                {
                    this.AddClient(client);

                    await client.RunAsync(cancellationToken).ContextFree();
                }
                finally
                {
                    this.RemoveClient(client);
                }
            }
        }

        protected override async Task ShutdownCoreAsync()
        {
            foreach (var startedEndPoint in this.startedEndpoints)
            {
                await startedEndPoint.StopAsync().ContextFree();
                startedEndPoint.Dispose();
            }
            this.startedEndpoints.Clear();

            await base.ShutdownCoreAsync().ContextFree();
        }

        protected override void StartCore()
        {
            var connectionHandler = new ConnectionHandler(this);
            foreach (var endPoint in this.endPoints)
            {
                var listener = endPoint.CreateListener(connectionHandler,
                    this.MaxRequestSize, this.MaxResponseSize);
                this.startedEndpoints.Add(listener);

                listener.Listen();
            }
        }

        private void AddClient(ClientPipeline client)
        {
            if (this.IsDisposed)
            {
                throw new ObjectDisposedException(this.ToString());
            }

            this.clients.TryAdd(client, client);
        }

        private void AddMethodDef(LightweightMethodStub methodStub)
        {
            this.methodDefinitions.Add(methodStub.OperationName, methodStub);
        }

        private void AddServiceDef(/*HashSet<string> registeredServices, */LightweightServerServiceDefinition serviceDef)
        {
            //if (!registeredServices.Add(serviceDef.ServiceName))
            //{
            //    // This is actually an internal error. It should have been checked earlier.
            //    throw new InvalidOperationException($"Service 'serviceDef.ServiceName' registered multiple times.");
            //}

            foreach (var methodStub in serviceDef.MethodStubs)
            {
                this.AddMethodDef(methodStub);
            }
        }

        private ILightweightServiceStubBuilder CreateServiceStubBuilder<TService>() where TService : class
        {
            // TODO: Try to abstract IOptions away somehow. My idea was that SciTech.Rpc should not depend 
            // on Microsoft.Extensions (except SciTech.Rpc.NetGrpc and SciTech.Rpc.DependencyInjection (of course)).
            IOptions<RpcServiceOptions<TService>>? options = this.ServiceProvider?.GetService<IOptions<RpcServiceOptions<TService>>>();
            return new LightweightServiceStubBuilder<TService>(options?.Value);
        }

        private void RemoveClient(ClientPipeline client) => this.clients.TryRemove(client, out _);

        private class ConnectionHandler : IRpcConnectionHandler
        {
            private readonly LightweightRpcServer server;

            public ConnectionHandler(LightweightRpcServer server)
            {
                this.server = server;
            }

            public ValueTask<byte[]?> HandleDatagramAsync(LightweightRpcEndPoint endPoint, byte[] data, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task RunPipelineClientAsync(IDuplexPipe clientPipe, LightweightRpcEndPoint endPoint, CancellationToken cancellationToken)
                => this.server.RunClientAsync(clientPipe, endPoint, cancellationToken);
        }

        private class MethodBinder : ILightweightMethodBinder
        {
            private LightweightRpcServer server;

            internal MethodBinder(LightweightRpcServer server)
            {
                this.server = server;
            }

            public void AddMethod(LightweightMethodStub methodStub)
            {
                this.server.AddMethodDef(methodStub);
            }
        }
    }
}
