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
using Microsoft.Extensions.Options;
using SciTech.Rpc.Grpc.Internal;
using SciTech.Rpc.Grpc.Server.Internal;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Logging;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.NetGrpc.Server.Internal
{
    internal interface INetGrpcBinder<TService> where TService : class
    {
        void AddServerStreamingMethod<TRequest, TResponse>(
            Method<TRequest, TResponse> create,
            ServerStreamingServerMethod<NetGrpcServiceActivator<TService>, TRequest, TResponse> handler)
            where TRequest : class
            where TResponse : class;

        public void AddUnaryMethod<TRequest, TResponse>(Method<TRequest, TResponse> create, UnaryServerMethod<NetGrpcServiceActivator<TService>, TRequest, TResponse> handler)
            where TRequest : class
            where TResponse : class;
    }

    /// <summary>
    /// Builds a type implementing server side stubs for a ASP.NET Core gRPC service defined by an RpcService
    /// interface. The service interface must be tagged with the <see cref="RpcServiceAttribute"/> attribute.
    /// Note, this class will only generate an implementation for the declared members of the service, nothing
    /// is generated for inherited members.
    /// </summary>
#pragma warning disable CA1812
    internal class NetGrpcServiceStubBuilder<TService>
        : RpcServiceStubBuilder<TService, INetGrpcBinder<TService>> where TService : class
#pragma warning restore CA1812
    {
        private static readonly ILog Logger = LogProvider.For<NetGrpcServiceStubBuilder<TService>>();

        public NetGrpcServiceStubBuilder(IOptions<RpcServiceOptions<TService>> options) :
            this(RpcBuilderUtil.GetServiceInfoFromType(typeof(TService)), options.Value)
        {

        }
        //internal NetGrpcServiceStubBuilder(IRpcSerializer serializer) :
        //    this(RpcBuilderUtil.GetServiceInfoFromType(typeof(TService)), serializer)
        //{
        //}

        public NetGrpcServiceStubBuilder(RpcServiceInfo serviceInfo, RpcServiceOptions<TService>? options)
            : base(serviceInfo, options)
        {
        }

        internal void Bind(NetGrpcServer server, ServiceMethodProviderContext<NetGrpcServiceActivator<TService>> providerContext)
        {
            var binder = new NetGrpcServiceStubBuilder<TService>.Binder(providerContext);
            this.GenerateOperationHandlers(server, binder);
        }

        protected override void AddEventHandlerDefinition<TEventArgs>(
            RpcEventInfo eventInfo,
            Func<RpcObjectRequest, IServiceProvider?, IRpcAsyncStreamWriter<TEventArgs>, IRpcCallContext, ValueTask> beginEventProducer,
            RpcStub<TService> serviceStub,
            INetGrpcBinder<TService> binder)
        {
            ServerStreamingServerMethod<NetGrpcServiceActivator<TService>, RpcObjectRequest, TEventArgs> handler = (activator, request, responseStream, context) =>
                beginEventProducer(request, activator.ServiceProvider, new GrpcAsyncStreamWriter<TEventArgs>(responseStream), new GrpcCallContext(context)).AsTask();

            var beginEventProducerName = $"Begin{eventInfo.Name}";

            binder.AddServerStreamingMethod(
                GrpcMethodDefinition.Create<RpcObjectRequest, TEventArgs>(
                    MethodType.ServerStreaming,
                    eventInfo.FullServiceName,
                    beginEventProducerName,
                    serviceStub.Serializer),
                handler);
        }

        protected override void AddGenericAsyncMethodImpl<TRequest, TReturn, TResponseReturn>(
            Func<TService, TRequest, CancellationToken, Task<TReturn>> serviceCaller,
            Func<TReturn, TResponseReturn>? responseConverter,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            INetGrpcBinder<TService> binder)
        {
            var serializer = serviceStub.Serializer;
            if (operationInfo.AllowFault)
            {
                Task<RpcResponseWithError<TResponseReturn>> Handler(NetGrpcServiceActivator<TService> activator, TRequest request, ServerCallContext context)
                    => serviceStub.CallAsyncMethodWithError(
                        request, activator.ServiceProvider, new GrpcCallContext(context), serviceCaller,
                        responseConverter, faultHandler, serializer).AsTask();

                var methodStub = GrpcMethodDefinition.Create<TRequest, RpcResponseWithError<TResponseReturn>>(
                    MethodType.Unary,
                    operationInfo.FullServiceName, operationInfo.Name,
                    serializer);

                binder.AddUnaryMethod(methodStub, Handler);
            }
            else
            {
                Task<RpcResponse<TResponseReturn>> Handler(NetGrpcServiceActivator<TService> activator, TRequest request, ServerCallContext context)
                    => serviceStub.CallAsyncMethod(
                        request, activator.ServiceProvider, new GrpcCallContext(context), serviceCaller,
                        responseConverter).AsTask();

                var methodStub = GrpcMethodDefinition.Create<TRequest, RpcResponse<TResponseReturn>>(
                    MethodType.Unary,
                    operationInfo.FullServiceName, operationInfo.Name,
                    serializer);

                binder.AddUnaryMethod(methodStub, Handler);
            }
        }

        protected override void AddGenericBlockingMethodImpl<TRequest, TReturn, TResponseReturn>(
            Func<TService, TRequest, CancellationToken, TReturn> serviceCaller,
            Func<TReturn, TResponseReturn>? responseConverter,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            INetGrpcBinder<TService> binder)
        {
            var serializer = serviceStub.Serializer;

            if (operationInfo.AllowFault)
            {
                Task<RpcResponseWithError<TResponseReturn>> Handler(NetGrpcServiceActivator<TService> activator, TRequest request, ServerCallContext context)
                    => serviceStub.CallBlockingMethodWithError(
                        request,
                        activator.ServiceProvider,
                        new GrpcCallContext(context),
                        serviceCaller,
                        responseConverter,
                        faultHandler,
                        serializer).AsTask();

                var methodStub = GrpcMethodDefinition.Create<TRequest, RpcResponseWithError<TResponseReturn>>(
                    MethodType.Unary,
                    operationInfo.FullServiceName, operationInfo.Name,
                    serializer);

                binder.AddUnaryMethod(methodStub, Handler);
            }
            else
            {
                Task<RpcResponse<TResponseReturn>> handler(NetGrpcServiceActivator<TService> activator, TRequest request, ServerCallContext context) =>
                    serviceStub.CallBlockingMethod(
                        request,
                        activator.ServiceProvider,
                        new GrpcCallContext(context),
                        serviceCaller,
                        responseConverter).AsTask();

                var methodStub = GrpcMethodDefinition.Create<TRequest, RpcResponse<TResponseReturn>>(
                    MethodType.Unary,
                    operationInfo.FullServiceName, operationInfo.Name,
                    serializer);

                binder.AddUnaryMethod(methodStub, handler);
            }
        }

        protected override void AddGenericVoidAsyncMethodImpl<TRequest>(
            Func<TService, TRequest, CancellationToken, Task> serviceCaller,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            INetGrpcBinder<TService> binder)
        {
            var serializer = serviceStub.Serializer;
            if (operationInfo.AllowFault)
            {
                Task<RpcResponseWithError> Handler(NetGrpcServiceActivator<TService> activator, TRequest request, ServerCallContext context)
                    => serviceStub.CallVoidAsyncMethodWithError(
                        request, activator.ServiceProvider, new GrpcCallContext(context), serviceCaller, faultHandler,
                        serializer).AsTask();

                var methodStub = GrpcMethodDefinition.Create<TRequest, RpcResponseWithError>(
                    MethodType.Unary,
                    operationInfo.FullServiceName, operationInfo.Name,
                    serializer);

                binder.AddUnaryMethod(methodStub, Handler);
            }
            else
            {
                Task<RpcResponseWithError> Handler(NetGrpcServiceActivator<TService> activator, TRequest request, ServerCallContext context)
                    => serviceStub.CallVoidAsyncMethodWithError(
                        request, activator.ServiceProvider, new GrpcCallContext(context), serviceCaller, faultHandler,
                        serializer).AsTask();

                var methodStub = GrpcMethodDefinition.Create<TRequest, RpcResponseWithError>(
                    MethodType.Unary,
                    operationInfo.FullServiceName, operationInfo.Name,
                    serializer);

                binder.AddUnaryMethod(methodStub, Handler);

            }
        }

        protected override void AddGenericVoidBlockingMethodImpl<TRequest>(
            Action<TService, TRequest, CancellationToken> serviceCaller,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            INetGrpcBinder<TService> binder)
        {
            var serializer = serviceStub.Serializer;
            if (operationInfo.AllowFault)
            {
                Task<RpcResponseWithError> handler(NetGrpcServiceActivator<TService> activator, TRequest request, ServerCallContext context)
                    => serviceStub.CallVoidBlockingMethodWithError(
                        request, activator.ServiceProvider, new GrpcCallContext(context), serviceCaller, faultHandler,
                        serializer).AsTask();

                var methodStub = GrpcMethodDefinition.Create<TRequest, RpcResponseWithError>(
                    MethodType.Unary,
                    operationInfo.FullServiceName, operationInfo.Name,
                    serializer);

                binder.AddUnaryMethod(methodStub, handler);
            }
            else
            {
                Task<RpcResponse> handler(NetGrpcServiceActivator<TService> activator, TRequest request, ServerCallContext context)
                    => serviceStub.CallVoidBlockingMethod(
                        request, activator.ServiceProvider, new GrpcCallContext(context), serviceCaller).AsTask();

                var methodStub = GrpcMethodDefinition.Create<TRequest, RpcResponse>(
                    MethodType.Unary,
                    operationInfo.FullServiceName, operationInfo.Name,
                    serializer);

                binder.AddUnaryMethod(methodStub, handler);

            }
        }

        protected override void AddServerStreamingMethodImpl<TRequest, TReturn, TResponseReturn>(
            Func<TService, TRequest, CancellationToken, IAsyncEnumerable<TReturn>> serviceCaller,
            Func<TReturn, TResponseReturn>? responseConverter,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            INetGrpcBinder<TService> binder)
        {
            var serializer = serviceStub.Serializer;
            ServerStreamingServerMethod<NetGrpcServiceActivator<TService>, TRequest, TResponseReturn> handler = (activator, request, responseStream, context) =>
            {
                return serviceStub.CallServerStreamingMethod(
                    request,
                    activator.ServiceProvider,
                    new GrpcCallContext(context),
                    new GrpcAsyncStreamWriter<TResponseReturn>(responseStream),
                    serviceCaller,
                    responseConverter,
                    faultHandler,
                    serializer).AsTask();
            };

            var methodStub = GrpcMethodDefinition.Create<TRequest, TResponseReturn>(
                MethodType.ServerStreaming,
                operationInfo.FullServiceName, operationInfo.Name,
                serializer);

            binder.AddServerStreamingMethod(methodStub, handler);
        }

        protected override ImmutableRpcServerOptions CreateStubOptions(IRpcServerImpl server)
        {
            var o = this.Options;
            var registeredOptions = server.ServiceDefinitionsProvider.GetServiceOptions(typeof(TService));
            if ((registeredOptions?.ReceiveMaxMessageSize != null && registeredOptions?.ReceiveMaxMessageSize != o?.ReceiveMaxMessageSize)
                || (registeredOptions?.SendMaxMessageSize != null && registeredOptions?.SendMaxMessageSize != o?.SendMaxMessageSize))
            {
                Logger.Warn("Message settings in registered options do not match provided options. Registered settings will be ignored.");
            }

            return ImmutableRpcServerOptions.Combine(this.Options, registeredOptions);
        }


        /// <summary>
        /// Small helper class, mainly just used to shorten the name 
        /// ServiceMethodProviderContext{NetGrpcServiceActivator{TService}} a little.
        /// </summary>
        internal sealed class Binder : INetGrpcBinder<TService>
        {
            private ServiceMethodProviderContext<NetGrpcServiceActivator<TService>> context;

            public Binder(ServiceMethodProviderContext<NetGrpcServiceActivator<TService>> context)
            {
                this.context = context ?? throw new ArgumentNullException(nameof(context));
            }

            public void AddServerStreamingMethod<TRequest, TResponse>(
                Method<TRequest, TResponse> create,
                ServerStreamingServerMethod<NetGrpcServiceActivator<TService>, TRequest, TResponse> handler)
                where TRequest : class
                where TResponse : class
            {
                this.context.AddServerStreamingMethod(create, new List<object>(), handler);
            }

            public void AddUnaryMethod<TRequest, TResponse>(Method<TRequest, TResponse> create, UnaryServerMethod<NetGrpcServiceActivator<TService>, TRequest, TResponse> handler)
                where TRequest : class
                where TResponse : class
            {
                this.context.AddUnaryMethod(create, new List<object>(), handler);
            }
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
}
