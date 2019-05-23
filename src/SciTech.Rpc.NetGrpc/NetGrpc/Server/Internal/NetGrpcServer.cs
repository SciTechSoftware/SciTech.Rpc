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


using Grpc.Core;
using SciTech.Rpc.Grpc.Server.Internal;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using SciTech.Rpc.Internal;
using System;
using System.Threading.Tasks;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.NetGrpc.Server.Internal
{
    internal sealed class NetGrpcServer : RpcServerBase
    {
        private static readonly Type[] GrpcServiceStubBuilderCtorArgTypes = new Type[] { typeof(IRpcSerializer) };

        private IRpcSerializer serializer;

        private ServiceBinderBase? serviceBinder;

        internal NetGrpcServer(RpcServicePublisher servicePublisher, IRpcServiceDefinitionsProvider serviceDefinitionsProvider, ServiceBinderBase serviceBinder, IRpcSerializer? serializer=null)
            : this(servicePublisher, servicePublisher, serviceDefinitionsProvider, serviceBinder, serializer)
        {
        }

        internal NetGrpcServer(
            IRpcServicePublisher servicePublisher,
            IRpcServiceActivator serviceImplProvider,
            IRpcServiceDefinitionsProvider serviceDefinitionsProvider,
            ServiceBinderBase serviceBinder,
            IRpcSerializer? serializer=null)
            : base(servicePublisher, serviceImplProvider, serviceDefinitionsProvider)
        {
            this.serializer = serializer ?? new ProtobufSerializer();
            this.serviceBinder = serviceBinder;
        }

        public IRpcSerializer Serializer
        {
            get => this.serializer;
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
            var serviceBinder = this.serviceBinder ?? throw new InvalidOperationException("BuildServiceStubs should only be called once.");
            var stubBuilder = this.CreateServiceStubBuilder(serviceType);
            stubBuilder.Bind(serviceBinder, this);
        }

        protected override void BuildServiceStubs()
        {
            this.ServiceDefinitionsProvider.Freeze();

            var serviceBinder = this.serviceBinder ?? throw new InvalidOperationException("BuildServiceStubs should only be called once.");

            var queryServiceMethodDef = new UnaryMethodStub<RpcObjectRequest, RpcServicesQueryResponse>(
                "__SciTech.Rpc.RpcService", "QueryServices",
                this.serializer,
                this.QueryServices);
            serviceBinder.AddMethod(queryServiceMethodDef, (UnaryServerMethod<RpcObjectRequest, RpcServicesQueryResponse>?)null);

            base.BuildServiceStubs();

            this.serviceBinder = null;
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
