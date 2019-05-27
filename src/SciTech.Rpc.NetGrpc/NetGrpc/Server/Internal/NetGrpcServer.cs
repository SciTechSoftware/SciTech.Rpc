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


using Microsoft.Extensions.Options;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using System;
using System.Threading.Tasks;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.NetGrpc.Server.Internal
{
    /// <summary>
    /// The ASP.NET Core gRPC implementation of <see cref="RpcServerBase"/>. Will not be directly used by client code, instead it is 
    /// registered using <see cref="NetGrpcEndpointRouteBuilderExtensions.MapNetGrpcServices"/>.
    /// </summary>
    internal sealed class NetGrpcServer : RpcServerBase
    {
        public NetGrpcServer(RpcServicePublisher servicePublisher, IOptions<RpcServiceOptions> options)
            : this(servicePublisher, servicePublisher, servicePublisher.DefinitionsProvider, options.Value)
        {

        }

        internal NetGrpcServer(
            IRpcServicePublisher servicePublisher,
            IRpcServiceActivator serviceImplProvider,
            IRpcServiceDefinitionsProvider serviceDefinitionsProvider,
            //ServiceMethodProviderContext<NetGrpcServiceActivator>? context,
            RpcServiceOptions? options)
            : base(servicePublisher, serviceImplProvider, serviceDefinitionsProvider, options)
        {
        }

        internal static Task<RpcServicesQueryResponse> QueryServices(NetGrpcServer server, RpcObjectRequest request, GrpcCore.ServerCallContext callContext)
        {
            return Task.FromResult(server.QueryServices(request.Id));
        }

        protected override void AddEndPoint(IRpcServerEndPoint endPoint)
        {
            throw new NotSupportedException("End points cannot be added to NetGrpc, use ASP.NET configuration instead.");
        }

        protected override void BuildServiceStub(Type serviceType)
        {
            throw new NotSupportedException("Service stubs should be built by NetGrpcServiceMethodProvider<TService>, using BuildServiceStub<TService>.");
        }

        protected override void BuildServiceStubs()
        {
            // Not much to do here. Service stubs are built by NetGrpcServiceMethodProvider.
            this.ServiceDefinitionsProvider.Freeze();
        }

        protected override void CheckCanStart()
        {
            base.CheckCanStart();
        }

        protected override IRpcSerializer CreateDefaultSerializer()
        {
            return new ProtobufSerializer();
        }

        protected override Task ShutdownCoreAsync()
        {
            return Task.CompletedTask;
        }

        protected override void StartCore()
        {
        }
    }
}
