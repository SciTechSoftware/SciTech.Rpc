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
using Grpc.Core;
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
    using NetGrpcMethodBinder = ServiceMethodProviderContext<NetGrpcServiceActivator>;

    internal interface INetGrpcServiceStubBuilder
    {
        void Bind(ServiceMethodProviderContext<NetGrpcServiceActivator> context, IRpcServerImpl server);
    }

#pragma warning disable CA1812
    /// <summary>
    /// Builds a type implementing server side stubs for a ASP.NET Core gRPC service defined by an RpcService
    /// interface. The service interface must be tagged with the <see cref="RpcServiceAttribute"/> attribute.
    /// Note, this class will only generate an implementation for the declared members of the service, nothing
    /// is generated for inherited members.
    /// </summary>
    internal class NetGrpcServiceStubBuilder<TService> : RpcServiceStubBuilder<TService, NetGrpcMethodBinder>, INetGrpcServiceStubBuilder where TService : class
    {
        public NetGrpcServiceStubBuilder(IRpcSerializer serializer) :
            this(RpcBuilderUtil.GetServiceInfoFromType(typeof(TService)), serializer)
        {
        }

        public NetGrpcServiceStubBuilder(RpcServiceInfo serviceInfo, IRpcSerializer serializer) : base(serviceInfo, serializer)
        {
        }

        public void Bind(ServiceMethodProviderContext<NetGrpcServiceActivator> context, IRpcServerImpl server)
        {
            this.GenerateOperationHandlers(server, context);

        }

        protected override void AddEventHandlerDefinition<TEventArgs>(
            RpcEventInfo eventInfo,
            Func<RpcObjectRequest, IServiceProvider?,
                IRpcAsyncStreamWriter<TEventArgs>, IRpcCallContext, ValueTask> beginEventProducer,
            RpcStub<TService> serviceStub,
            NetGrpcMethodBinder binder)
        {
            ServerStreamingServerMethod<NetGrpcServiceActivator, RpcObjectRequest, TEventArgs> handler = (activator, request, responseStream, context) =>
                beginEventProducer(request, activator.ServiceProvider, new GrpcAsyncStreamWriter<TEventArgs>(responseStream), new GrpcCallContext(context)).AsTask();

            var beginEventProducerName = $"Begin{eventInfo.Name}";

            binder.AddServerStreamingMethod(
                GrpcMethodDefinition.Create<RpcObjectRequest, TEventArgs>(
                    MethodType.ServerStreaming,
                    eventInfo.FullServiceName,
                    beginEventProducerName,
                    this.serializer),
                new List<object>(),
                handler);
        }

        protected override void AddGenericAsyncMethodImpl<TRequest, TReturn, TResponseReturn>(
            Func<TService, TRequest, Task<TReturn>> serviceCaller,
            Func<TReturn, TResponseReturn>? responseConverter,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            NetGrpcMethodBinder binder)
        {
            var serializer = this.serializer;
            UnaryServerMethod<NetGrpcServiceActivator, TRequest, RpcResponse<TResponseReturn>> handler = (activator, request, context) =>
            {
                return serviceStub.CallAsyncMethod(
                    request,
                    activator.ServiceProvider,
                    new GrpcCallContext(context),
                    serviceCaller,
                    responseConverter,
                    faultHandler,
                    serializer).AsTask();
            };

            var methodStub = GrpcMethodDefinition.Create<TRequest, RpcResponse<TResponseReturn>>(
                MethodType.Unary,
                operationInfo.FullServiceName, operationInfo.Name,
                this.serializer);

            binder.AddUnaryMethod<TRequest, RpcResponse<TResponseReturn>>(
                methodStub,
                new List<object>(),
                handler);
        }

        protected override void AddGenericBlockingMethodImpl<TRequest, TReturn, TResponseReturn>(
            Func<TService, TRequest, TReturn> serviceCaller,
            Func<TReturn, TResponseReturn>? responseConverter,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            NetGrpcMethodBinder binder)
        {
            var serializer = this.serializer;
            UnaryServerMethod<NetGrpcServiceActivator, TRequest, RpcResponse<TResponseReturn>> handler = (activator, request, context) =>
             {
                 return serviceStub.CallBlockingMethod(request, activator.ServiceProvider, new GrpcCallContext(context), serviceCaller, responseConverter, faultHandler, serializer).AsTask();
             };

            var methodStub = GrpcMethodDefinition.Create<TRequest, RpcResponse<TResponseReturn>>(
                MethodType.Unary,
                operationInfo.FullServiceName, operationInfo.Name,
                serializer);

            binder.AddUnaryMethod(methodStub, new List<object>(), handler);
        }

        protected override void AddGenericVoidAsyncMethodImpl<TRequest>(
            Func<TService, TRequest, Task> serviceCaller,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            NetGrpcMethodBinder binder)
        {
            var serializer = this.serializer;
            UnaryServerMethod<NetGrpcServiceActivator, TRequest, RpcResponse> handler = (activator, request, context) =>
             {
                 return serviceStub.CallVoidAsyncMethod(request, activator.ServiceProvider, new GrpcCallContext(context), serviceCaller, faultHandler, serializer).AsTask();
             };

            var methodStub = GrpcMethodDefinition.Create<TRequest, RpcResponse>(
                MethodType.Unary,
                operationInfo.FullServiceName, operationInfo.Name,
                this.serializer);

            binder.AddUnaryMethod(methodStub, new List<object>(), handler);
        }

        protected override void AddGenericVoidBlockingMethodImpl<TRequest>(
            Action<TService, TRequest> serviceCaller,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            NetGrpcMethodBinder binder)
        {
            var serializer = this.serializer;
            UnaryServerMethod<NetGrpcServiceActivator, TRequest, RpcResponse> handler = (activator, request, context) =>
             {
                 return serviceStub.CallVoidBlockingMethod(request, activator.ServiceProvider, new GrpcCallContext(context), serviceCaller, faultHandler, serializer).AsTask();
             };

            var methodStub = GrpcMethodDefinition.Create<TRequest, RpcResponse>(
                MethodType.Unary,
                operationInfo.FullServiceName, operationInfo.Name,
                this.serializer);

            binder.AddUnaryMethod(methodStub, new List<object>(), handler);
        }

        protected override RpcStub<TService> CreateServiceStub(IRpcServerImpl server)
        {
            return new RpcStub<TService>(server);
        }

        private sealed class GrpcAsyncStreamWriter<T> : IRpcAsyncStreamWriter<T>
        {
            private readonly GrpcCore.IAsyncStreamWriter<T> streamWriter;

            public GrpcAsyncStreamWriter(GrpcCore.IAsyncStreamWriter<T> streamWriter)
            {
                this.streamWriter = streamWriter;
            }

            public Task WriteAsync(T message)
            {
                return this.streamWriter.WriteAsync(message);
            }
        }
    }
#pragma warning restore CA1812
}
