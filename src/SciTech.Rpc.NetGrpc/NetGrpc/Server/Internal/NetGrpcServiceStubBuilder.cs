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

using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using SciTech.Rpc.Internal;
using System;
using System.Threading.Tasks;
using GrpcCore = Grpc.Core;
using SciTech.Rpc.Grpc.Server.Internal;

namespace SciTech.Rpc.NetGrpc.Server.Internal
{
    internal interface INetGrpcServiceStubBuilder
    {
        void Bind(ServiceBinderBase binder, IRpcServerImpl server);
    }

#pragma warning disable CA1812
    /// <summary>
    /// The GrpcServiceBuilder builds a type implementing server side stubs for a gRPC service defined by an RpcService
    /// interface. The service interface must be tagged with the <see cref="RpcServiceAttribute"/> attribute.
    /// Note, this class will only generate an implementation for the declared members of the service, nothing
    /// is generated for inherited members.
    /// </summary>
    internal class NetGrpcServiceStubBuilder<TService> : RpcServiceStubBuilder<TService, ServiceBinderBase>, INetGrpcServiceStubBuilder where TService : class
    {
        public NetGrpcServiceStubBuilder(IRpcSerializer serializer) :
            this(RpcBuilderUtil.GetServiceInfoFromType(typeof(TService)), serializer)
        {
        }

        public NetGrpcServiceStubBuilder(RpcServiceInfo serviceInfo, IRpcSerializer serializer) : base(serviceInfo, serializer)
        {
        }

        public void Bind(ServiceBinderBase binder, IRpcServerImpl server)
        {
            this.GenerateOperationHandlers(server, binder);

        }

        protected override void AddEventHandlerDefinition<TEventArgs>(
            RpcEventInfo eventInfo,
            Func<RpcObjectRequest, IServiceProvider?, IRpcAsyncStreamWriter<TEventArgs>, IRpcCallContext, ValueTask> beginEventProducer,
            RpcStub<TService> serviceStub,
            ServiceBinderBase binder)
        {
            ServerStreamingServerMethod<NetGrpcServiceActivator, RpcObjectRequest, TEventArgs> handler = (activator, request, responseStream, context) =>
                beginEventProducer(request, activator.ServiceProvider, new GrpcAsyncStreamWriter<TEventArgs>(responseStream), new GrpcCallContext(context)).AsTask();

            var beginEventProducerName = $"Begin{eventInfo.Name}";

            binder.AddMethod(
                new ServerStreamingMethodStub<RpcObjectRequest, TEventArgs>(
                    eventInfo.FullServiceName,
                    beginEventProducerName,
                    this.serializer,
                    handler),
                (GrpcCore.ServerStreamingServerMethod<RpcObjectRequest, TEventArgs>?)null);
        }

        protected override void AddGenericAsyncMethodImpl<TRequest, TReturn, TResponseReturn>(
            Func<TService, TRequest, Task<TReturn>> serviceCaller,
            Func<TReturn, TResponseReturn>? responseConverter,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            ServiceBinderBase binder)
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

            var methodStub = new UnaryMethodStub<TRequest, RpcResponse<TResponseReturn>>(
                operationInfo.FullServiceName, operationInfo.Name,
                this.serializer,
                handler);

            binder.AddMethod(
                methodStub,
                (GrpcCore.UnaryServerMethod<TRequest, RpcResponse<TResponseReturn>>?)null);
        }

        protected override void AddGenericBlockingMethodImpl<TRequest, TReturn, TResponseReturn>(
            Func<TService, TRequest, TReturn> serviceCaller,
            Func<TReturn, TResponseReturn>? responseConverter,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            ServiceBinderBase binder)
        {
            var serializer = this.serializer;
            UnaryServerMethod<NetGrpcServiceActivator, TRequest, RpcResponse<TResponseReturn>> handler = (activator, request, context) =>
             {
                 return serviceStub.CallBlockingMethod(request, activator.ServiceProvider, new GrpcCallContext(context), serviceCaller, responseConverter, faultHandler, serializer).AsTask();
             };

            var methodStub = new UnaryMethodStub<TRequest, RpcResponse<TResponseReturn>>(
                operationInfo.FullServiceName, operationInfo.Name,
                serializer,
                handler);

            binder.AddMethod(
                methodStub,
                (GrpcCore.UnaryServerMethod<TRequest, RpcResponse<TResponseReturn>>?)null);
        }

        protected override void AddGenericVoidAsyncMethodImpl<TRequest>(
            Func<TService, TRequest, Task> serviceCaller,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            ServiceBinderBase binder)
        {
            var serializer = this.serializer;
            UnaryServerMethod<NetGrpcServiceActivator, TRequest, RpcResponse> handler = (activator, request, context) =>
             {
                 return serviceStub.CallVoidAsyncMethod(request, activator.ServiceProvider, new GrpcCallContext(context), serviceCaller, faultHandler, serializer).AsTask();
             };

            var methodStub = new UnaryMethodStub<TRequest, RpcResponse>(
                operationInfo.FullServiceName, operationInfo.Name,
                this.serializer,
                handler);

            binder.AddMethod(
                methodStub,
                (GrpcCore.UnaryServerMethod<TRequest, RpcResponse>?)null);
        }

        protected override void AddGenericVoidBlockingMethodImpl<TRequest>(
            Action<TService, TRequest> serviceCaller,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            ServiceBinderBase binder)
        {
            var serializer = this.serializer;
            UnaryServerMethod<NetGrpcServiceActivator, TRequest, RpcResponse> handler = (activator, request, context) =>
             {
                 return serviceStub.CallVoidBlockingMethod(request, activator.ServiceProvider, new GrpcCallContext(context), serviceCaller, faultHandler, serializer).AsTask();
             };

            var methodStub = new UnaryMethodStub<TRequest, RpcResponse>(
                operationInfo.FullServiceName, operationInfo.Name,
                serializer,
                handler);

            binder.AddMethod(
                methodStub,
                (GrpcCore.UnaryServerMethod<TRequest, RpcResponse>?)null);
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

    internal abstract class NetGrpcMethodStub<TRequest, TResponse> : GrpcCore.Method<TRequest, TResponse>
    {
#nullable disable
        protected NetGrpcMethodStub(
            MethodType methodType,
            string serviceName, string name,
            IRpcSerializer serializer)
            : base(methodType, serviceName, name,
                  GrpcCore.Marshallers.Create(
                    serializer: serializer.ToBytes,
                    deserializer: serializer.FromBytes<TRequest>
                ),
                  GrpcCore.Marshallers.Create(
                    serializer: serializer.ToBytes,
                    deserializer: serializer.FromBytes<TResponse>
                ))
        {
        }
#nullable restore
    }

    internal sealed class ServerStreamingMethodStub<TRequest, TResponse> : NetGrpcMethodStub<TRequest, TResponse>
    {
        public ServerStreamingMethodStub(
            string serviceName, string name,
            IRpcSerializer serializer,
            ServerStreamingServerMethod<NetGrpcServiceActivator, TRequest, TResponse> invoker)
            : base(MethodType.ServerStreaming, serviceName, name, serializer)
        {
            this.Invoker = invoker;
        }

        public ServerStreamingServerMethod<NetGrpcServiceActivator, TRequest, TResponse> Invoker { get; }
    }

    internal sealed class UnaryMethodStub<TRequest, TResponse> : NetGrpcMethodStub<TRequest, TResponse>
    {
        public UnaryMethodStub(
            string serviceName, string name,
            IRpcSerializer serializer,
            UnaryServerMethod<NetGrpcServiceActivator, TRequest, TResponse> invoker)
            : base(MethodType.Unary, serviceName, name, serializer)
        {
            this.Invoker = invoker;
        }

        public UnaryServerMethod<NetGrpcServiceActivator, TRequest, TResponse> Invoker { get; }
    }
}
