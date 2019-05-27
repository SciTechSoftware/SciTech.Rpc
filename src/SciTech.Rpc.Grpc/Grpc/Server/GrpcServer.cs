#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion


using SciTech.Rpc.Grpc.Server.Internal;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.Grpc.Server
{
    public sealed class GrpcServer : RpcServerBase
    {
        private static readonly Type[] GrpcServiceStubBuilderCtorArgTypes = new Type[] { typeof(IRpcSerializer) };

        private List<GrpcServerEndPoint> endPoints = new List<GrpcServerEndPoint>();

        private GrpcCore.Server? grpcServer = new GrpcCore.Server();

        private IRpcSerializer serializer;

        public GrpcServer(IRpcServiceDefinitionsProvider definitionsProvider, IServiceProvider? serviceProvider = null, RpcServiceOptions? options= null)
            : this(RpcServerId.Empty, definitionsProvider, serviceProvider, options)
        {
        }

        public GrpcServer(RpcServicePublisher servicePublisher, IServiceProvider? serviceProvider = null, RpcServiceOptions? options = null)
            : this(servicePublisher, servicePublisher, servicePublisher.DefinitionsProvider, serviceProvider, options)
        {
        }

        public GrpcServer(RpcServerId serverId, IRpcServiceDefinitionsProvider definitionsProvider, IServiceProvider? serviceProvider = null, RpcServiceOptions? options = null)
            : this(new RpcServicePublisher(definitionsProvider, serverId), serviceProvider, options)
        {
        }

        /// <summary>
        /// Internal use, only intended for testing.
        /// </summary>
        /// <param name="servicePublisher"></param>
        /// <param name="serviceImplProvider"></param>
        /// <param name="serviceDefinitionsProvider"></param>
        /// <param name="serializer"></param>
        public GrpcServer(
            IRpcServicePublisher servicePublisher,
            IRpcServiceActivator serviceImplProvider,
            IRpcServiceDefinitionsProvider serviceDefinitionsProvider,
            IServiceProvider? serviceProvider,
            RpcServiceOptions? options = null)
            : base(servicePublisher, serviceImplProvider, serviceDefinitionsProvider, options)
        {
            this.ServiceProvider = serviceProvider;
            this.serializer = options?.Serializer ?? new ProtobufSerializer();
        }

        public IRpcSerializer Serializer
        {
            get => this.serializer;
        }

        protected override IServiceProvider? ServiceProvider { get; }

        /// <summary>
        /// Initializes the connection info for this server. If the <see cref="IRpcServicePublisher.ConnectionInfo"/> for 
        /// the service publisher has not been initialized, this method will initialize that as well.
        /// If the publisher connection info is initialized, the server id of the provided <see cref="RpcServerConnectionInfo"/>
        /// must match the publisher id.
        /// </summary>
        public void AddEndPoint(GrpcServerEndPoint endPoint)
        {
            bool firstEndPoint = false;
            lock (this.syncRoot)
            {
                this.CheckIsInitializing();

                firstEndPoint = this.endPoints.Count == 0;
                this.endPoints.Add(endPoint);
            }

            if (firstEndPoint)
            {
                this.ServicePublisher.TryInitConnectionInfo(endPoint.GetConnectionInfo(RpcServerId.Empty));
            }
        }

        internal Task<RpcServicesQueryResponse> QueryServices(RpcObjectRequest request, GrpcCore.ServerCallContext context)
        {
            return Task.FromResult(this.QueryServices(request.Id));
        }

        protected override void AddEndPoint(IRpcServerEndPoint endPoint)
        {
            if (endPoint is GrpcServerEndPoint grpcEndPoint)
            {
                this.AddEndPoint(grpcEndPoint);
            }
            else
            {
                throw new ArgumentException($"End point must implement {nameof(GrpcServerEndPoint)}.");
            }
        }

        protected override void BuildServiceStub(Type serviceType)
        {
            var server = this.grpcServer ?? throw new InvalidOperationException("BuildServiceStub should not be called after shutdown");
            var stubBuilder = this.CreateServiceStubBuilder(serviceType);
            var serviceDef = stubBuilder.Build(this);

            server.Services.Add(serviceDef);
        }

        protected override void BuildServiceStubs()
        {
            var grpcServer = this.grpcServer ?? throw new InvalidOperationException("BuildServiceStubs should not be called after shutdown");

            var queryServiceMethodDef = GrpcMethodDefinition.Create<RpcObjectRequest, RpcServicesQueryResponse>(GrpcCore.MethodType.Unary,
                "__SciTech.Rpc.RpcService", "QueryServices",
                this.serializer);

            var rpcServiceBuilder = new GrpcCore.ServerServiceDefinition.Builder();
            rpcServiceBuilder.AddMethod(queryServiceMethodDef, this.QueryServices);
            var rpcServiceDef = rpcServiceBuilder.Build();
            grpcServer.Services.Add(rpcServiceDef);

            base.BuildServiceStubs();
        }

        protected override void CheckCanStart()
        {
            base.CheckCanStart();
        }

        protected async override Task ShutdownCoreAsync()
        {
            GrpcCore.Server? grpcServer;
            lock (this.syncRoot)
            {
                grpcServer = this.grpcServer;
                this.grpcServer = null;
            }

            if (grpcServer != null)
            {
                await grpcServer.ShutdownAsync().ContextFree();
            }
        }

        protected override void StartCore()
        {
            this.ServiceDefinitionsProvider.Freeze();

            var grpcServer = this.grpcServer ?? throw new InvalidOperationException("StartCore should not be called after shutdown");

            foreach (var endPoint in this.endPoints)
            {
                var port = endPoint.CreateServerPort();
                grpcServer.Ports.Add(port);
            }

            grpcServer.Start();
        }

        private IGrpcServiceStubBuilder CreateServiceStubBuilder(Type serviceType)
        {
            return (IGrpcServiceStubBuilder)typeof(GrpcServiceStubBuilder<>).MakeGenericType(serviceType)
                .GetConstructor(GrpcServiceStubBuilderCtorArgTypes)
                .Invoke(new object[] { this.serializer });
        }
    }
}
