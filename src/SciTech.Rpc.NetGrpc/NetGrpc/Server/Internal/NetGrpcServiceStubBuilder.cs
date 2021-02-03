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

using Grpc.AspNetCore.Server.Model;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using SciTech.Rpc.Authorization;
using SciTech.Rpc.Grpc.Internal;
using SciTech.Rpc.Grpc.Server.Internal;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Logging;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.NetGrpc.Server.Internal
{
    internal interface INetGrpcBinder<TService> where TService : class
    {
        void AddServerStreamingMethod<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            ImmutableArray<object> metadata,
            ServerStreamingServerMethod<NetGrpcServiceActivator<TService>, TRequest, TResponse> handler)
            where TRequest : class
            where TResponse : class;

        public void AddUnaryMethod<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            ImmutableArray<object> metadata,
            UnaryServerMethod<NetGrpcServiceActivator<TService>, TRequest, TResponse> handler)
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
            Func<RpcObjectRequest, IServiceProvider?, IRpcAsyncStreamWriter<TEventArgs>, IRpcContext, ValueTask> beginEventProducer,
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
                eventInfo.Metadata,
                handler);
        }

        protected override void AddGenericAsyncMethodCore<TRequest, TReturn, TResponseReturn>(
            Func<TService, TRequest, CancellationToken, Task<TReturn>> serviceCaller,
            Func<TReturn, TResponseReturn>? responseConverter,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            INetGrpcBinder<TService> binder)
        {
            var serializer = serviceStub.Serializer;
            Task<RpcResponse<TResponseReturn>> Handler(NetGrpcServiceActivator<TService> activator, TRequest request, ServerCallContext context)
                => serviceStub.CallAsyncMethod(
                    request, activator.ServiceProvider, new GrpcCallContext(context), serviceCaller,
                    responseConverter, faultHandler, serializer).AsTask();

            var methodStub = GrpcMethodDefinition.Create<TRequest, RpcResponse<TResponseReturn>>(
                MethodType.Unary,
                operationInfo.FullServiceName, operationInfo.Name,
                serializer);

            binder.AddUnaryMethod(methodStub, operationInfo.Metadata, Handler);
        }

        protected override void AddGenericBlockingMethodCore<TRequest, TReturn, TResponseReturn>(
            Func<TService, TRequest, CancellationToken, TReturn> serviceCaller,
            Func<TReturn, TResponseReturn>? responseConverter,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            INetGrpcBinder<TService> binder)
        {
            var serializer = serviceStub.Serializer;

            Task<RpcResponse<TResponseReturn>> Handler(NetGrpcServiceActivator<TService> activator, TRequest request, ServerCallContext context)
                => serviceStub.CallBlockingMethod(
                    request,
                    new GrpcCallContext(context),
                    serviceCaller,
                    responseConverter,
                    faultHandler,
                    serializer,
                    activator.ServiceProvider).AsTask();

            var methodStub = GrpcMethodDefinition.Create<TRequest, RpcResponse<TResponseReturn>>(
                MethodType.Unary,
                operationInfo.FullServiceName, operationInfo.Name,
                serializer);

            binder.AddUnaryMethod(methodStub, operationInfo.Metadata, Handler);
        }

        protected override void AddGenericVoidAsyncMethodCore<TRequest>(
            Func<TService, TRequest, CancellationToken, Task> serviceCaller,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            INetGrpcBinder<TService> binder)
        {
            var serializer = serviceStub.Serializer;
            Task<RpcResponse> Handler(NetGrpcServiceActivator<TService> activator, TRequest request, ServerCallContext context)
                => serviceStub.CallVoidAsyncMethod(
                    request, activator.ServiceProvider, new GrpcCallContext(context), serviceCaller, faultHandler,
                    serializer).AsTask();

            var methodStub = GrpcMethodDefinition.Create<TRequest, RpcResponse>(
                MethodType.Unary,
                operationInfo.FullServiceName, operationInfo.Name,
                serializer);

            binder.AddUnaryMethod(methodStub, operationInfo.Metadata, Handler);
        }

        protected override void AddGenericVoidBlockingMethodCore<TRequest>(
            Action<TService, TRequest, CancellationToken> serviceCaller,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            INetGrpcBinder<TService> binder)
        {
            var serializer = serviceStub.Serializer;
            Task<RpcResponse> handler(NetGrpcServiceActivator<TService> activator, TRequest request, ServerCallContext context)
                => serviceStub.CallVoidBlockingMethod(
                    request, activator.ServiceProvider, new GrpcCallContext(context), serviceCaller, faultHandler,
                    serializer).AsTask();

            var methodStub = GrpcMethodDefinition.Create<TRequest, RpcResponse>(
                MethodType.Unary,
                operationInfo.FullServiceName, operationInfo.Name,
                serializer);

            binder.AddUnaryMethod(methodStub, operationInfo.Metadata, handler);
        }

        protected override void AddServerStreamingMethodCore<TRequest, TReturn, TResponseReturn>(
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

            binder.AddServerStreamingMethod(methodStub, operationInfo.Metadata, handler);
        }

        protected override void AddCallbackMethodCore<TRequest, TReturn, TResponseReturn>(
            Func<TService, TRequest, Action<TReturn>, CancellationToken, Task> serviceCaller,
            Func<TReturn, TResponseReturn>? responseConverter, RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub, RpcOperationInfo operationInfo, INetGrpcBinder<TService> binder)
        {
            var serializer = serviceStub.Serializer;
            ServerStreamingServerMethod<NetGrpcServiceActivator<TService>, TRequest, TResponseReturn> handler = (activator, request, responseStream, context) =>
            {
                return serviceStub.CallCallbackMethod(
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

            binder.AddServerStreamingMethod(methodStub, operationInfo.Metadata, handler);
        }

        protected override ImmutableRpcServerOptions CreateStubOptions(IRpcServerCore server)
        {
            var o = this.Options;
            var registeredOptions = server.ServiceDefinitionsProvider.GetServiceOptions(typeof(TService));
            if ((registeredOptions?.ReceiveMaxMessageSize != null && registeredOptions?.ReceiveMaxMessageSize != o?.ReceiveMaxMessageSize)
                || (registeredOptions?.SendMaxMessageSize != null && registeredOptions?.SendMaxMessageSize != o?.SendMaxMessageSize))
            {
                // TODO: Logger.Warn("Message settings in registered options do not match provided options. Registered settings will be ignored.");
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
                ImmutableArray<object> metadata,
                ServerStreamingServerMethod<NetGrpcServiceActivator<TService>, TRequest, TResponse> handler)
                where TRequest : class
                where TResponse : class
            {
                this.context.AddServerStreamingMethod(create, TranslateMetadata(metadata), handler);
            }

            public void AddUnaryMethod<TRequest, TResponse>(Method<TRequest, TResponse> create, ImmutableArray<object> metadata, UnaryServerMethod<NetGrpcServiceActivator<TService>, TRequest, TResponse> handler)
                where TRequest : class
                where TResponse : class
            {
                this.context.AddUnaryMethod(create, TranslateMetadata(metadata), handler);
            }

            private static IList<object> TranslateMetadata(ImmutableArray<object> metadata)
            {
                if( !metadata.IsDefaultOrEmpty)
                {
                    var translatedMetadata = new List<object>(metadata.Length);
                    foreach( var metadataEntry in metadata )
                    {
                        object translatedEntry = metadataEntry;
                        if( metadataEntry is RpcAuthorizeAttribute rpcAuthorize)
                        {
                            translatedEntry = new AuthorizeAttribute
                            {
                                AuthenticationSchemes = rpcAuthorize.AuthenticationSchemes,
                                Policy = rpcAuthorize.Policy,
                                Roles = rpcAuthorize.Roles                               
                            };
                        } else if( metadataEntry is RpcAllowAnonymousAttribute rpcAllowAnonymous)
                        {
                            translatedEntry = new AllowAnonymousAttribute();
                        }

                        translatedMetadata.Add(translatedEntry);
                    }

                    return translatedMetadata;
                }

                return Array.Empty<object>();
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
