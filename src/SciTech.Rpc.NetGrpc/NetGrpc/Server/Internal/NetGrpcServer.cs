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


using Grpc.AspNetCore.Server.Model;
using SciTech.Rpc.Grpc.Server.Internal;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using System;
using System.Collections.Generic;
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
        private static readonly Type[] GrpcServiceStubBuilderCtorArgTypes = new Type[] { typeof(IRpcSerializer) };

        private ServiceMethodProviderContext<NetGrpcServiceActivator>? context;

        private IRpcSerializer serializer;

        internal NetGrpcServer(RpcServicePublisher servicePublisher, ServiceMethodProviderContext<NetGrpcServiceActivator> context, RpcServiceOptions? options)
            : this(servicePublisher, servicePublisher, servicePublisher.DefinitionsProvider, context, options)
        {
        }

        internal NetGrpcServer(
            IRpcServicePublisher servicePublisher,
            IRpcServiceActivator serviceImplProvider,
            IRpcServiceDefinitionsProvider serviceDefinitionsProvider,
            ServiceMethodProviderContext<NetGrpcServiceActivator> context,
            RpcServiceOptions? options)
            : base(servicePublisher, serviceImplProvider, serviceDefinitionsProvider, options)
        {
            this.serializer = options?.Serializer ?? new ProtobufSerializer();
            this.context = context;
        }

        internal Task<RpcServicesQueryResponse> QueryServices(NetGrpcServiceActivator _, RpcObjectRequest request, GrpcCore.ServerCallContext context)
        {
            return Task.FromResult(this.QueryServices(request.Id));
        }

        protected override void AddEndPoint(IRpcServerEndPoint endPoint)
        {
            throw new NotSupportedException("End points cannot be added to NetGrpc, use ASP.NET configuration instead.");
        }

        protected override void BuildServiceStub(Type serviceType)
        {
            var context = this.context ?? throw new InvalidOperationException("BuildServiceStubs should only be called once.");
            var stubBuilder = this.CreateServiceStubBuilder(serviceType);
            stubBuilder.Bind(context, this);
        }

        protected override void BuildServiceStubs()
        {
            this.ServiceDefinitionsProvider.Freeze();

            var context = this.context ?? throw new InvalidOperationException("BuildServiceStubs should only be called once.");

            var queryServiceMethodDef = GrpcMethodDefinition.Create<RpcObjectRequest, RpcServicesQueryResponse>(
                GrpcCore.MethodType.Unary,
                "__SciTech.Rpc.RpcService", "QueryServices",
                this.serializer);
            context.AddUnaryMethod(queryServiceMethodDef, new List<object>(), this.QueryServices);

            base.BuildServiceStubs();

            this.context = null;
        }

        protected override void CheckCanStart()
        {
            base.CheckCanStart();
        }

        protected override Task ShutdownCoreAsync()
        {
            return Task.CompletedTask;
        }

        protected override void StartCore()
        {
        }

        private INetGrpcServiceStubBuilder CreateServiceStubBuilder(Type serviceType)
        {
            return (INetGrpcServiceStubBuilder)typeof(NetGrpcServiceStubBuilder<>).MakeGenericType(serviceType)
                .GetConstructor(GrpcServiceStubBuilderCtorArgTypes)
                .Invoke(new object[] { this.serializer });
        }
    }
}
