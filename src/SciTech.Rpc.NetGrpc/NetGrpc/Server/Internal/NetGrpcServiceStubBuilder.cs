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
    /// <summary>
    /// Builds a type implementing server side stubs for a ASP.NET Core gRPC service defined by an RpcService
    /// interface. The service interface must be tagged with the <see cref="RpcServiceAttribute"/> attribute.
    /// Note, this class will only generate an implementation for the declared members of the service, nothing
    /// is generated for inherited members.
    /// </summary>
#pragma warning disable CA1812
    internal class NetGrpcServiceStubBuilder<TService>
        : RpcServiceStubBuilder<TService, NetGrpcServiceStubBuilder<TService>.Binder> where TService : class
#pragma warning restore CA1812
    {
        private static readonly ILog Logger = LogProvider.For<NetGrpcServiceStubBuilder<TService>>();

        private readonly NetGrpcServer server;

        public NetGrpcServiceStubBuilder(NetGrpcServer server, IOptions<RpcServiceOptions<TService>> options) :
            this(server, RpcBuilderUtil.GetServiceInfoFromType(typeof(TService)), options.Value)
        {

        }
        //internal NetGrpcServiceStubBuilder(IRpcSerializer serializer) :
        //    this(RpcBuilderUtil.GetServiceInfoFromType(typeof(TService)), serializer)
        //{
        //}

        public NetGrpcServiceStubBuilder(NetGrpcServer server, RpcServiceInfo serviceInfo, RpcServiceOptions<TService>? options)
            : base(serviceInfo, options)
        {
            this.server = server;
        }

        internal void Bind(ServiceMethodProviderContext<NetGrpcServiceActivator<TService>> providerContext)
        {
            var binder = new NetGrpcServiceStubBuilder<TService>.Binder(providerContext);
            this.GenerateOperationHandlers(this.server, binder);
        }

        protected override void AddEventHandlerDefinition<TEventArgs>(
            RpcEventInfo eventInfo,
            Func<RpcObjectRequest, IServiceProvider?,
                IRpcAsyncStreamWriter<TEventArgs>, IRpcCallContext, ValueTask> beginEventProducer,
            RpcStub<TService> serviceStub,
            Binder binder)
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
            Binder binder)
        {
            var serializer = serviceStub.Serializer;
            UnaryServerMethod<NetGrpcServiceActivator<TService>, TRequest, RpcResponse<TResponseReturn>> handler = (activator, request, context) =>
            {
                context.CancellationToken.Register(this.CallCancelled);
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
                serializer);

            binder.AddUnaryMethod<TRequest, RpcResponse<TResponseReturn>>(
                methodStub,
                handler);
        }

        protected override void AddGenericBlockingMethodImpl<TRequest, TReturn, TResponseReturn>(
            Func<TService, TRequest, CancellationToken, TReturn> serviceCaller,
            Func<TReturn, TResponseReturn>? responseConverter,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            Binder binder)
        {
            var serializer = serviceStub.Serializer;
            UnaryServerMethod<NetGrpcServiceActivator<TService>, TRequest, RpcResponse<TResponseReturn>> handler =
                (activator, request, context) =>
                {
                    context.CancellationToken.Register(this.CallCancelled);

                    return serviceStub.CallBlockingMethod(
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
                serializer);

            binder.AddUnaryMethod(methodStub, handler);
        }

        protected override void AddGenericVoidAsyncMethodImpl<TRequest>(
            Func<TService, TRequest, CancellationToken, Task> serviceCaller,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            Binder binder)
        {
            var serializer = serviceStub.Serializer;
            UnaryServerMethod<NetGrpcServiceActivator<TService>, TRequest, RpcResponse> handler = (activator, request, context) =>
             {
                 context.CancellationToken.Register(this.CallCancelled);

                 return serviceStub.CallVoidAsyncMethod(request, activator.ServiceProvider, new GrpcCallContext(context), serviceCaller, faultHandler, serializer).AsTask();
             };

            var methodStub = GrpcMethodDefinition.Create<TRequest, RpcResponse>(
                MethodType.Unary,
                operationInfo.FullServiceName, operationInfo.Name,
                serializer);

            binder.AddUnaryMethod(methodStub, handler);
        }

        protected override void AddGenericVoidBlockingMethodImpl<TRequest>(
            Action<TService, TRequest, CancellationToken> serviceCaller,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            Binder binder)
        {
            var serializer = serviceStub.Serializer;
            UnaryServerMethod<NetGrpcServiceActivator<TService>, TRequest, RpcResponse> handler = (activator, request, context) =>
             {
                 context.CancellationToken.Register(this.CallCancelled);

                 return serviceStub.CallVoidBlockingMethod(request, activator.ServiceProvider, new GrpcCallContext(context), serviceCaller, faultHandler, serializer).AsTask();
             };

            var methodStub = GrpcMethodDefinition.Create<TRequest, RpcResponse>(
                MethodType.Unary,
                operationInfo.FullServiceName, operationInfo.Name,
                serializer);

            binder.AddUnaryMethod(methodStub, handler);
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

        private void CallCancelled()
        {

        }

        /// <summary>
        /// Small helper class, mainly just used to shorten the name 
        /// ServiceMethodProviderContext{NetGrpcServiceActivator{TService}} a little.
        /// </summary>
        internal class Binder
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
