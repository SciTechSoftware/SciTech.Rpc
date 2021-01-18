#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Lightweight.Server.Internal;
using SciTech.Rpc.Logging;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Serialization.Internal;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using SciTech.Threading;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
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

        private readonly ConcurrentDictionary<ClientPipeline, ClientPipeline> clients
            = new ConcurrentDictionary<ClientPipeline, ClientPipeline>();

        private readonly Dictionary<string, LightweightMethodStub> methodDefinitions
            = new Dictionary<string, LightweightMethodStub>();

        private List<LightweightRpcEndPoint> endPoints = new List<LightweightRpcEndPoint>();

        private List<ILightweightRpcListener> startedEndpoints = new List<ILightweightRpcListener>();

        public LightweightRpcServer(
            IServiceProvider? serviceProvider = null,
            IRpcServerOptions? options = null,
            LightweightOptions? lightweightOptions = null,
            ILogger<LightweightRpcServer>? logger=null)
            : this(RpcServerId.NewId(), null, serviceProvider, options, lightweightOptions, logger)
        {
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="servicePublisher"></param>
        public LightweightRpcServer(
            RpcServicePublisher servicePublisher,
            IServiceProvider? serviceProvider = null,
            IRpcServerOptions? options = null,
            LightweightOptions? lightweightOptions = null,
            ILogger<LightweightRpcServer>? logger = null)
            : this(servicePublisher ?? throw new ArgumentNullException(nameof(servicePublisher)),
                  servicePublisher,
                  servicePublisher.DefinitionsProvider, serviceProvider, options, lightweightOptions, logger)
        {
        }

        public LightweightRpcServer(
            RpcServerId serverId,
            IRpcServiceDefinitionsProvider? definitionsProvider = null,
            IServiceProvider? serviceProvider = null,
            IRpcServerOptions? options = null,
            LightweightOptions? lightweightOptions = null,
            ILogger<LightweightRpcServer>? logger = null)
            : this(new RpcServicePublisher(definitionsProvider ?? new RpcServiceDefinitionsBuilder(), serverId),
                  serviceProvider, options, lightweightOptions, logger)
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
            IRpcServerOptions? options,
            LightweightOptions? lightweightOptions = null,
            ILogger<LightweightRpcServer>? logger=null)
            : base(servicePublisher, serviceImplProvider, definitionsProvider, 
                  options, 
                  logger ?? RpcLogger.CreateLogger<LightweightRpcServer>())
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


        public override void AddEndPoint(IRpcServerEndPoint endPoint)
        {
            if (endPoint is LightweightRpcEndPoint lightweightEndPoint)
            {
                this.AddEndPoint(lightweightEndPoint);
            } else
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
            bool isStopped = this.State == ServerState.Stopped || this.State == ServerState.Failed;

            if( !isStopped )
            {
                this.Logger.LogWarning("Synchronous dispose while RPC server is running.");
                // Hopefully this will not dead-lock.
                this.ShutdownAsync().AwaiterResult();
            }

            base.Dispose(disposing);
        }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cleanup")]
        protected async override ValueTask DisposeAsyncCore()
        {
            await this.ShutdownAsync().ContextFree();

            foreach (var client in this.clients)
            {
                try 
                {
                    await client.Key.DisposeAsync().ContextFree(); 
                } catch (Exception x) { 
                    this.Logger.LogWarning(x, "Error when disposing client.");
                }
            }

            this.clients.Clear();

            await base.DisposeAsyncCore().ContextFree();
        }

        protected virtual ValueTask OnReceiveAsync(IMemoryOwner<byte> message) => default;

        private async Task RunClientAsync(IDuplexPipe pipe, LightweightRpcEndPoint endPoint, IPrincipal? user)
        {
            var client = new ClientPipeline(pipe, this, endPoint, user, this.MaxRequestSize, this.MaxResponseSize, this.KeepSizeLimitedConnectionAlive);
            try
            {
                try
                {
                    this.AddClient(client);

                    await client.RunAsync().ContextFree();
                }
                finally
                {
                    await client.DisposeAsync().ContextFree();
                }
            }
            finally
            {
                // Late removal of client (after dispose and wait) to avoid that shut down returns
                // before all clients have ended. Make sure that a client is not accidentally used
                // after dispose.
                this.RemoveClient(client);

            }
        }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Returns null on failure")]
        private async ValueTask<byte[]?> HandleDatagramAsync(LightweightRpcEndPoint endPoint, byte[] data, CancellationToken cancellationToken)
        {
            if (LightweightRpcFrame.TryRead(data, this.MaxRequestSize, out var frame) == RpcFrameState.Full)
            {
                if (frame.FrameType != RpcFrameType.UnaryRequest)
                {
                    this.Logger.LogWarning("Datagram only handles unary requests.");
                    return null;
                }

                var methodStub = this.GetMethodDefinition(frame.RpcOperation);
                if (methodStub == null)
                {
                    this.Logger.LogWarning("Unknown operation '{Operation}' in datagram frame.", frame.RpcOperation);
                    return null;
                }

                CancellationToken actualCancellationToken;
                CancellationTokenSource? timeoutCts = null;
                CancellationTokenSource? linkedCts = null;
                if (frame.Timeout > 0)
                {
                    timeoutCts = new CancellationTokenSource();
                    timeoutCts.CancelAfter((int)frame.Timeout);
                    if( cancellationToken.CanBeCanceled)
                    {
                        linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
                        actualCancellationToken = linkedCts.Token;
                    } else
                    {
                        actualCancellationToken = timeoutCts.Token;
                    }
                } else
                {
                    actualCancellationToken = cancellationToken;
                }

                try
                {
                    var context = new LightweightCallContext(endPoint, null, frame.Headers, actualCancellationToken );

                    using IServiceScope? scope = this.ServiceProvider?.CreateScope();
                    using var frameWriter = new LightweightRpcFrameWriter(65536);
                    await methodStub.HandleMessage(frameWriter, frame, scope?.ServiceProvider, context).ContextFree();

                    return frameWriter.GetFrameData();
                }
                catch( Exception x )
                {
                    this.Logger.LogWarning(x, "Error occurred in HandleDatagramAsync.");
                }
                finally
                {
                    linkedCts?.Dispose();
                    timeoutCts?.Dispose();
                }
            }
            else
            {
                this.Logger.LogInformation("Received incomplete datagram frame.");
            }

            return null;
        }

        protected override async Task ShutdownCoreAsync()
        {
            foreach (var startedEndPoint in this.startedEndpoints)
            {
                await startedEndPoint.StopAsync().ContextFree();
                await startedEndPoint.DisposeAsync().ContextFree();
            }
            this.startedEndpoints.Clear();

            var clients = this.clients.ToArray();
            this.clients.Clear();

            var disposeTasks = clients.Select(p => p.Key.DisposeAsync().AsTask()).ToList();
            await Task.WhenAll(disposeTasks).ContextFree();

            foreach( var client in clients)
            {
                await client.Key.WaitFinishedAsync().ContextFree();
            }

            Debug.Assert(this.clients.IsEmpty, "Client should not be added after Shutdown has been called.");

            await base.ShutdownCoreAsync().ContextFree();
        }

        protected override void StartCore()
        {
            this.ServiceDefinitionsProvider.Freeze();   // Dynamic services not yet implemented.
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
            if (this.IsStopped)
            {
                throw new ObjectDisposedException(this.ToString());
            }

            this.clients.TryAdd(client, client);
        }

        private void AddMethodDef(LightweightMethodStub methodStub)
        {
            this.Logger.LogInformation("Add service operation {Operation}.", methodStub.OperationName);

            this.methodDefinitions.Add(methodStub.OperationName, methodStub);
        }

        private void AddServiceDef(/*HashSet<string> registeredServices, */LightweightServerServiceDefinition serviceDef)
        {
            this.Logger.LogInformation("Build service stub for {Service}.", serviceDef.ServiceName);
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

        private void RemoveClient(ClientPipeline client)
        {
            this.clients.TryRemove(client, out _);
        }

        private class ConnectionHandler : IRpcConnectionHandler
        {
            private readonly LightweightRpcServer server;

            internal ConnectionHandler(LightweightRpcServer server)
            {
                this.server = server;
            }

            public ValueTask<byte[]?> HandleDatagramAsync(LightweightRpcEndPoint endPoint, byte[] data, CancellationToken cancellationToken)
                => this.server.HandleDatagramAsync(endPoint, data, cancellationToken);


            public Task RunPipelineClientAsync(IDuplexPipe clientPipe, LightweightRpcEndPoint endPoint, IPrincipal? user)
                => this.server.RunClientAsync(clientPipe, endPoint, user);
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
