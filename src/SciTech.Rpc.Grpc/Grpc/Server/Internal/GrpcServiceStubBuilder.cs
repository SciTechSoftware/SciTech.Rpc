﻿#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using Microsoft.Extensions.DependencyInjection;
using SciTech.Rpc.Grpc.Internal;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.Grpc.Server.Internal
{
    internal interface IGrpcServiceStubBuilder
    {
        GrpcCore.ServerServiceDefinition Build(IRpcServerImpl server);
    }

    /// <summary>
    /// The GrpcServiceBuilder builds a type implementing server side stubs for a gRPC service defined by an RpcService
    /// interface. The service interface must be tagged with the <see cref="RpcServiceAttribute"/> attribute.
    /// Note, this class will only generate an implementation for the declared members of the service, nothing
    /// is generated for inherited members.
    /// </summary>
    internal class GrpcServiceStubBuilder<TService> : RpcServiceStubBuilder<TService, IGrpcMethodBinder>, IGrpcServiceStubBuilder where TService : class
    {
        public GrpcServiceStubBuilder(RpcServiceOptions<TService>? options) :
            this(RpcBuilderUtil.GetServiceInfoFromType(typeof(TService)), options)
        {
        }

        public GrpcServiceStubBuilder(RpcServiceInfo serviceInfo, RpcServiceOptions<TService>? options) : base(serviceInfo, options)
        {
        }

        public GrpcCore.ServerServiceDefinition Build(IRpcServerImpl server)
        {
            var grpcServiceBuilder = new GrpcCore.ServerServiceDefinition.Builder();
            var binder = new GrpcMethodBinder(grpcServiceBuilder);

            this.GenerateOperationHandlers(server, binder);

            var serviceDefinition = grpcServiceBuilder.Build();
            return serviceDefinition;
        }

        protected override void AddEventHandlerDefinition<TEventArgs>(
            RpcEventInfo eventInfo,
            Func<RpcObjectRequest, IServiceProvider?, IRpcAsyncStreamWriter<TEventArgs>, IRpcCallContextWithCancellation, ValueTask> beginEventProducer,
            RpcStub<TService> serviceStub,
            IGrpcMethodBinder binder)
        {
            GrpcCore.ServerStreamingServerMethod<RpcObjectRequest, TEventArgs> handler = (request, responseStream, context) =>
            {
                using (var scope = CreateServiceScope(serviceStub))
                {
                    return beginEventProducer(request,
                        scope?.ServiceProvider,
                        new GrpcAsyncStreamWriter<TEventArgs>(responseStream),
                        new GrpcCallContext(context)).AsTask();
                }
            };

            var beginEventProducerName = $"Begin{eventInfo.Name}";

            binder.AddMethod(
                GrpcMethodDefinition.Create<RpcObjectRequest, TEventArgs>(
                    GrpcCore.MethodType.ServerStreaming,
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
            IGrpcMethodBinder binder)
        {
            var serializer = serviceStub.Serializer;
            if (operationInfo.AllowFault)
            {
                GrpcCore.UnaryServerMethod<TRequest, RpcResponseWithError<TResponseReturn>> handler = (request, context) =>
                {
                    using (var callScope = serviceStub.ServiceProvider?.CreateScope())
                    {
                        return serviceStub.CallAsyncMethodWithError(request, callScope?.ServiceProvider, new GrpcCallContext(context), serviceCaller, responseConverter, faultHandler, serializer).AsTask();
                    }
                };

                binder.AddMethod(
                    GrpcMethodDefinition.Create<TRequest, RpcResponseWithError<TResponseReturn>>(GrpcCore.MethodType.Unary,
                        operationInfo.FullServiceName, operationInfo.Name, serializer),
                    handler);
            } else
            {
                GrpcCore.UnaryServerMethod<TRequest, RpcResponse<TResponseReturn>> handler = (request, context) =>
                {
                    try
                    {
                        using (var callScope = serviceStub.ServiceProvider?.CreateScope())
                        {
                            return serviceStub.CallAsyncMethod(request, callScope?.ServiceProvider, new GrpcCallContext(context), serviceCaller, responseConverter).AsTask();
                        }
                    }
                    catch(Exception e)
                    {
                        //serviceStub.Server.HandleError();
                        throw;
                    }
                };

                binder.AddMethod(
                    GrpcMethodDefinition.Create<TRequest, RpcResponse<TResponseReturn>>(GrpcCore.MethodType.Unary,
                        operationInfo.FullServiceName, operationInfo.Name, serializer),
                    handler);

            }
        }

        protected override void AddGenericBlockingMethodImpl<TRequest, TReturn, TResponseReturn>(
            Func<TService, TRequest, CancellationToken, TReturn> serviceCaller,
            Func<TReturn, TResponseReturn>? responseConverter,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            IGrpcMethodBinder binder)
        {
            var serializer = serviceStub.Serializer;
            if (operationInfo.AllowFault)
            {
                Task<RpcResponseWithError<TResponseReturn>> handler(TRequest request, GrpcCore.ServerCallContext context)
                {
                    using (var serviceScope = CreateServiceScope(serviceStub))
                    {
                        return serviceStub.CallBlockingMethodWithError(
                            request, new GrpcCallContext(context), serviceCaller, responseConverter,
                            faultHandler, serializer, serviceScope?.ServiceProvider).AsTask();
                    }
                }

                binder.AddMethod(
                    GrpcMethodDefinition.Create<TRequest, RpcResponseWithError<TResponseReturn>>(GrpcCore.MethodType.Unary,
                        operationInfo.FullServiceName, operationInfo.Name, serializer),
                    handler);
            } else
            {
                Task<RpcResponse<TResponseReturn>> handler(TRequest request, GrpcCore.ServerCallContext context)
                {
                    using var serviceScope = CreateServiceScope(serviceStub);
                    
                    return serviceStub.CallBlockingMethod(
                        request, new GrpcCallContext(context), serviceCaller, responseConverter,                        
                        serviceScope?.ServiceProvider).AsTask();
                }

                binder.AddMethod(
                    GrpcMethodDefinition.Create<TRequest, RpcResponse<TResponseReturn>>(GrpcCore.MethodType.Unary,
                        operationInfo.FullServiceName, operationInfo.Name, serializer),
                    handler);

            }
        }

        protected override void AddGenericVoidAsyncMethodImpl<TRequest>(
            Func<TService, TRequest, CancellationToken, Task> serviceCaller,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            IGrpcMethodBinder binder)
        {
            var serializer = serviceStub.Serializer;
            if (operationInfo.AllowFault)
            {
                GrpcCore.UnaryServerMethod<TRequest, RpcResponseWithError> handler = (request, context) =>
                {
                    using (var serviceScope = CreateServiceScope(serviceStub))
                    {
                        return serviceStub.CallVoidAsyncMethodWithError(request,
                            serviceScope?.ServiceProvider,
                            new GrpcCallContext(context), serviceCaller, faultHandler, serializer).AsTask();
                    }
                };

                binder.AddMethod(
                    GrpcMethodDefinition.Create<TRequest, RpcResponseWithError>(GrpcCore.MethodType.Unary,
                        operationInfo.FullServiceName, operationInfo.Name, serializer),
                    handler);
            } else
            {
                GrpcCore.UnaryServerMethod<TRequest, RpcResponse> handler = (request, context) =>
                {
                    using (var serviceScope = CreateServiceScope(serviceStub))
                    {
                        return serviceStub.CallVoidAsyncMethod(request,
                            serviceScope?.ServiceProvider,
                            new GrpcCallContext(context), serviceCaller).AsTask();
                    }
                };

                binder.AddMethod(
                    GrpcMethodDefinition.Create<TRequest, RpcResponse>(GrpcCore.MethodType.Unary,
                        operationInfo.FullServiceName, operationInfo.Name, serializer),
                    handler);

            }
        }

        protected override void AddGenericVoidBlockingMethodImpl<TRequest>(
            Action<TService, TRequest, CancellationToken> serviceCaller,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            IGrpcMethodBinder binder)
        {
            var serializer = serviceStub.Serializer;
            if (operationInfo.AllowFault)
            {
                GrpcCore.UnaryServerMethod<TRequest, RpcResponseWithError> handler = (request, context) =>
                {
                    using (var serviceScope = CreateServiceScope(serviceStub))
                    {
                        return serviceStub.CallVoidBlockingMethodWithError(
                            request, serviceScope?.ServiceProvider, new GrpcCallContext(context), serviceCaller,
                            faultHandler, serializer).AsTask();
                    }
                };

                binder.AddMethod(
                    GrpcMethodDefinition.Create<TRequest, RpcResponseWithError>(GrpcCore.MethodType.Unary,
                        operationInfo.FullServiceName, operationInfo.Name, serializer),
                    handler);
            } else
            {
                GrpcCore.UnaryServerMethod<TRequest, RpcResponse> handler = (request, context) =>
                {
                    using (var serviceScope = CreateServiceScope(serviceStub))
                    {
                        return serviceStub.CallVoidBlockingMethod(
                            request, serviceScope?.ServiceProvider, new GrpcCallContext(context), serviceCaller).AsTask();
                    }
                };

                binder.AddMethod(
                    GrpcMethodDefinition.Create<TRequest, RpcResponse>(GrpcCore.MethodType.Unary,
                        operationInfo.FullServiceName, operationInfo.Name, serializer),
                    handler);

            }
        }

        protected override void AddServerStreamingMethodImpl<TRequest, TReturn, TResponseReturn>(
            Func<TService, TRequest, CancellationToken, IAsyncEnumerable<TReturn>> serviceCaller,
            Func<TReturn, TResponseReturn>? responseConverter,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            IGrpcMethodBinder binder)
        {
            var serializer = serviceStub.Serializer;

            GrpcCore.ServerStreamingServerMethod<TRequest, TResponseReturn> handler = (request, responseStream, context) =>
            {
                using (var serviceScope = CreateServiceScope(serviceStub))
                {
                    return serviceStub.CallServerStreamingMethod(
                        request,
                        serviceScope?.ServiceProvider,
                        new GrpcCallContext(context),
                        new GrpcAsyncStreamWriter<TResponseReturn>(responseStream),
                        serviceCaller,
                        responseConverter,
                        faultHandler,
                        serializer).AsTask();
                }
            };

            binder.AddMethod(
                GrpcMethodDefinition.Create<TRequest, TResponseReturn>(
                    GrpcCore.MethodType.ServerStreaming,
                    operationInfo.FullServiceName,
                     operationInfo.Name,
                    serviceStub.Serializer),
                handler);
        }

        private static IServiceScope? CreateServiceScope(RpcStub stub)
        {
            // TODO: Maybe RpcStub should have the server as a type
            // parameter to avoid this cast?
            return stub.Server.ServiceProvider?.CreateScope();
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
