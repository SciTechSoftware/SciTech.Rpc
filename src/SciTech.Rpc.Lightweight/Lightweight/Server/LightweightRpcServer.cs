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
    public partial class LightweightRpcServer : RpcServerBase
    {
        private static readonly MethodInfo CreateServiceStubBuilderMethod = typeof(LightweightRpcServer).GetMethod(nameof(CreateServiceStubBuilder), BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly ConcurrentDictionary<Client, Client> clients = new ConcurrentDictionary<Client, Client>();

        private readonly Dictionary<string, LightweightMethodStub> methodDefinitions = new Dictionary<string, LightweightMethodStub>();

        private List<LightweightRpcEndPoint> endPoints = new List<LightweightRpcEndPoint>();

        private List<ILightweightRpcListener> startedEndpoints = new List<ILightweightRpcListener>();

        public LightweightRpcServer(IRpcServiceDefinitionsProvider definitionsProvider, IServiceProvider? serviceProvider, RpcServiceOptions options)
            : this(RpcServerId.NewId(), definitionsProvider, serviceProvider, options)
        {
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="servicePublisher"></param>
        public LightweightRpcServer(RpcServicePublisher servicePublisher, IServiceProvider? serviceProvider, RpcServiceOptions options)
            : this(servicePublisher ?? throw new ArgumentNullException(nameof(servicePublisher)),
                  servicePublisher,
                  servicePublisher.DefinitionsProvider, serviceProvider, options)
        {
        }

        public LightweightRpcServer(RpcServerId serverId, IRpcServiceDefinitionsProvider definitionsProvider, IServiceProvider? serviceProvider, RpcServiceOptions options)
            : this(new RpcServicePublisher(definitionsProvider, serverId), serviceProvider, options)
        {
        }

        /// <summary>
        /// Only intended for testing.
        /// </summary>
        public LightweightRpcServer(
            IRpcServicePublisher servicePublisher, IRpcServiceActivator serviceImplProvider,
            IRpcServiceDefinitionsProvider definitionsProvider, IServiceProvider? serviceProvider,
            RpcServiceOptions options)
            : base(servicePublisher, serviceImplProvider, definitionsProvider, options)
        {
            this.ServiceProvider = serviceProvider;
            this.MaxRequestSize = options?.ReceiveMaxMessageSize ?? this.ServiceDefinitionsProvider.Options.ReceiveMaxMessageSize ?? LightweightRpcFrame.DefaultMaxFrameLength;
            this.MaxResponseSize = options?.SendMaxMessageSize ?? this.ServiceDefinitionsProvider.Options.SendMaxMessageSize ?? LightweightRpcFrame.DefaultMaxFrameLength;
        }

        public int ClientCount => this.clients.Count;

        protected override IServiceProvider? ServiceProvider { get; }

        private int MaxRequestSize { get; }

        private int MaxResponseSize { get; }

        public void AddEndPoint(LightweightRpcEndPoint endPoint)
        {
            if (endPoint == null)
            {
                throw new ArgumentNullException(nameof(endPoint));
            }

            lock (this.syncRoot)
            {
                this.CheckIsInitializing();
                this.endPoints.Add(endPoint);
            }

            this.ServicePublisher.TryInitConnectionInfo(endPoint.GetConnectionInfo(this.ServicePublisher.ServerId));

        }

        internal LightweightMethodStub GetMethodDefinition(string rpcOperation)
        {
            this.methodDefinitions.TryGetValue(rpcOperation, out var methodStub);
            return methodStub;
        }

        protected override void AddEndPoint(IRpcServerEndPoint endPoint)
        {
            if (endPoint is LightweightRpcEndPoint lightweightEndPoint)
            {
                this.AddEndPoint(lightweightEndPoint);
            }
            else
            {
                throw new ArgumentException($"End point must implement {nameof(LightweightRpcEndPoint)}.");
            }
        }

        protected override void BuildServiceStub(Type serviceType)
        {
            var typedMethod = CreateServiceStubBuilderMethod.MakeGenericMethod(serviceType);
            var stubBuilder = (ILightweightServiceStubBuilder)typedMethod.Invoke(this, null);
            var serviceDef = stubBuilder.Build(this);

            this.AddServiceDef(serviceDef);
        }

        protected override void BuildServiceStubs()
        {
            var methodStub = new LightweightMethodStub<RpcObjectRequest, RpcServicesQueryResponse>(
                "SciTech.Rpc.RpcService.QueryServices",
                (request, _, context) => new ValueTask<RpcServicesQueryResponse>(this.QueryServices(request.Id)),
            this.Serializer, null);

            this.methodDefinitions.Add("SciTech.Rpc.RpcService.QueryServices", methodStub);

            base.BuildServiceStubs();
        }

        protected override IRpcSerializer CreateDefaultSerializer()
        {
            return new DataContractRpcSerializer();
        }

        protected override void Dispose(bool disposing)
        {
            if (!this.IsDisposed)
            {
                foreach (var client in this.clients)
                {
                    client.Key.Dispose();
                }
                this.clients.Clear();
            }

            base.Dispose(disposing);
        }

        protected virtual ValueTask OnReceiveAsync(IMemoryOwner<byte> message) => default;

        protected Task RunClientAsync(IDuplexPipe pipe, CancellationToken cancellationToken = default)
        {
            var client = new Client(pipe, this, this.MaxRequestSize, this.MaxResponseSize);

            // TODO: Add to clients list.
            return client.RunAsync(cancellationToken);
        }

        protected async override Task ShutdownCoreAsync()
        {
            foreach (var startedEndPoint in this.startedEndpoints)
            {
                await startedEndPoint.StopAsync().ContextFree();
            }

            await base.ShutdownCoreAsync().ContextFree();
        }

        protected override void StartCore()
        {
            Task ConnectedCallback(IDuplexPipe clientPipe)
            {
                return this.RunClientAsync(clientPipe);
            }

            foreach (var endPoint in this.endPoints)
            {
                var listener = endPoint.CreateListener(ConnectedCallback, this.MaxRequestSize, this.MaxResponseSize);
                this.startedEndpoints.Add(listener);

                listener.Listen();
            }
        }

        private void AddClient(Client client)
        {
            if (this.IsDisposed)
            {
                throw new ObjectDisposedException(this.ToString());
            }

            this.clients.TryAdd(client, client);
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
                this.methodDefinitions.Add(methodStub.OperationName, methodStub);
            }
        }

        private ILightweightServiceStubBuilder CreateServiceStubBuilder<TService>() where TService : class
        {
            IOptions<RpcServiceOptions<TService>>? options = this.ServiceProvider?.GetService<IOptions<RpcServiceOptions<TService>>>();
            return new LightweightServiceStubBuilder<TService>(options?.Value);
        }

        private void RemoveClient(Client client) => this.clients.TryRemove(client, out _);
    }
}
