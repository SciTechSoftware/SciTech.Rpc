﻿#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using SciTech.Rpc.Internal;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server.Internal
{
    internal interface ILightweightMethodBinder
    {
        void AddMethod(LightweightMethodStub methodStub);
    }

    internal interface ILightweightServiceStubBuilder
    {
        LightweightServerServiceDefinition Build(LightweightRpcServer server);
    }

    public class LightweightServerServiceDefinition
    {
        internal LightweightServerServiceDefinition(string serviceName, ImmutableArray<LightweightMethodStub> methodStubs)
        {
            this.ServiceName = serviceName;
            this.MethodStubs = methodStubs;

        }

        public string ServiceName { get; }

        internal ImmutableArray<LightweightMethodStub> MethodStubs { get; }
    }

    /// <summary>
    /// The <see cref="LightweightServiceStubBuilder{TService}"/> builds a type implementing server side stubs for an RPC service 
    /// defined by an RpcService interface. 
    /// The service interface must be tagged with the <see cref="RpcServiceAttribute"/> attribute.
    /// Note, this class will only generate an implementation for the declared members of the service, nothing
    /// is generated for inherited members.
    /// </summary>
    internal class LightweightServiceStubBuilder<TService>
        : RpcServiceStubBuilder<TService, ILightweightMethodBinder>, ILightweightServiceStubBuilder
        where TService : class
    {
        public LightweightServiceStubBuilder(RpcServiceOptions<TService>? options) :
            this(RpcBuilderUtil.GetServiceInfoFromType(typeof(TService)), options)
        {
        }

        public LightweightServiceStubBuilder(RpcServiceInfo serviceInfo, RpcServiceOptions<TService>? options)
            : base(serviceInfo, options)
        {

        }

        public LightweightServerServiceDefinition Build(LightweightRpcServer server)
        {
            var binder = new Binder();
            this.GenerateOperationHandlers(server, binder);

            return new LightweightServerServiceDefinition(this.ServiceInfo.FullName, binder.GetMethodStubs());
        }

        protected override void AddEventHandlerDefinition<TEventArgs>(
            RpcEventInfo eventInfo,
            Func<RpcObjectRequest, IServiceProvider?, IRpcAsyncStreamWriter<TEventArgs>, IRpcContext, ValueTask> beginEventProducer,
            RpcStub<TService> serviceStub,
            ILightweightMethodBinder binder)
        {
            var beginEventProducerName = $"{eventInfo.FullServiceName}.Begin{eventInfo.Name}";

            var methodStub = new LightweightStreamingMethodStub<RpcObjectRequest, TEventArgs>(beginEventProducerName, beginEventProducer, serviceStub.Serializer, null);

            binder.AddMethod(methodStub);
        }

        protected override void AddGenericAsyncMethodCore<TRequest, TReturn, TResponseReturn>(
            Func<TService, TRequest, CancellationToken, Task<TReturn>> serviceCaller,
            Func<TReturn, TResponseReturn>? responseConverter,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            ILightweightMethodBinder binder)
        {
            var serializer = operationInfo.SerializerOverride ?? serviceStub.Serializer;

            ValueTask<RpcResponse<TResponseReturn>> HandleRequest(TRequest request, IServiceProvider? serviceProvider, LightweightCallContext context)
                => serviceStub.CallAsyncMethod(request, serviceProvider, context, serviceCaller, responseConverter, faultHandler, serializer);

            var methodStub = new LightweightMethodStub<TRequest, RpcResponse<TResponseReturn>>(operationInfo.FullName, HandleRequest, serializer, faultHandler,
                operationInfo.AllowInlineExecution);

            binder.AddMethod(methodStub);
        }

        protected override void AddGenericBlockingMethodCore<TRequest, TReturn, TResponseReturn>(
            Func<TService, TRequest, CancellationToken, TReturn> serviceCaller,
            Func<TReturn, TResponseReturn>? responseConverter,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            ILightweightMethodBinder binder)
        {
            var serializer = operationInfo.SerializerOverride ?? serviceStub.Serializer;

            ValueTask<RpcResponse<TResponseReturn>> HandleRequest(TRequest request, IServiceProvider? serviceProvider, LightweightCallContext context)
                => serviceStub.CallBlockingMethod(request, context, serviceCaller, responseConverter, faultHandler, serializer, serviceProvider);

            var methodStub = new LightweightMethodStub<TRequest, RpcResponse<TResponseReturn>>(operationInfo.FullName, HandleRequest, serializer, faultHandler,
                operationInfo.AllowInlineExecution);
            binder.AddMethod(methodStub);
        }

        protected override void AddGenericVoidAsyncMethodCore<TRequest>(
            Func<TService, TRequest, CancellationToken, Task> serviceCaller,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            ILightweightMethodBinder binder)
        {
            var serializer = operationInfo.SerializerOverride ?? serviceStub.Serializer;
            ValueTask<RpcResponse> HandleRequest(TRequest request, IServiceProvider? serviceProvider, LightweightCallContext context)
                => serviceStub.CallVoidAsyncMethod(request, serviceProvider, context, serviceCaller, faultHandler, serializer);

            var methodStub = new LightweightMethodStub<TRequest, RpcResponse>(operationInfo.FullName, HandleRequest, serializer, faultHandler,
                operationInfo.AllowInlineExecution);
            binder.AddMethod(methodStub);
        }

        protected override void AddGenericVoidBlockingMethodCore<TRequest>(
            Action<TService, TRequest, CancellationToken> serviceCaller,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            ILightweightMethodBinder binder)
        {
            var serializer = operationInfo.SerializerOverride ?? serviceStub.Serializer;

            ValueTask<RpcResponse> HandleRequest(TRequest request, IServiceProvider? serviceProvider, LightweightCallContext context)
                => serviceStub.CallVoidBlockingMethod(request, serviceProvider, context, serviceCaller, faultHandler, serializer);

            var methodStub = new LightweightMethodStub<TRequest, RpcResponse>(operationInfo.FullName, HandleRequest, serializer, faultHandler,
                operationInfo.AllowInlineExecution);
            binder.AddMethod(methodStub);
        }


        protected override void AddServerStreamingMethodCore<TRequest, TReturn, TResponseReturn>(
            Func<TService, TRequest, CancellationToken, IAsyncEnumerable<TReturn>> serviceCaller,
            Func<TReturn, TResponseReturn>? responseConverter,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            ILightweightMethodBinder binder)
        {
            var serializer = serviceStub.Serializer ;

            ValueTask HandleRequest(TRequest request, IServiceProvider? serviceProvider, IRpcAsyncStreamWriter<TResponseReturn> responseWriter, LightweightCallContext context)
                => serviceStub.CallServerStreamingMethod(request, serviceProvider, context, responseWriter, serviceCaller, responseConverter, faultHandler, serializer);
            
            var methodStub = new LightweightStreamingMethodStub<TRequest, TResponseReturn>(operationInfo.FullName, HandleRequest, serializer, faultHandler);
            binder.AddMethod(methodStub);
        }

        protected override void AddCallbackMethodCore<TRequest, TReturn,TResponse>(
            Func<TService, TRequest, Action<TReturn>, CancellationToken, Task> serviceCaller,
            Func<TReturn, TResponse>? responseConverter,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub, RpcOperationInfo operationInfo, ILightweightMethodBinder binder)
        {
            var serializer = serviceStub.Serializer;

            ValueTask HandleRequest(TRequest request, IServiceProvider? serviceProvider, IRpcAsyncStreamWriter<TResponse> responseWriter, LightweightCallContext context)
                => serviceStub.CallCallbackMethod(request, serviceProvider, context, responseWriter, serviceCaller, responseConverter, faultHandler, serializer);

            var methodStub = new LightweightStreamingMethodStub<TRequest, TResponse>(operationInfo.FullName, HandleRequest, serializer, faultHandler);
            binder.AddMethod(methodStub);
        }



        private class Binder : ILightweightMethodBinder
        {
            private List<LightweightMethodStub> methodStubs = new List<LightweightMethodStub>();

            public Binder()
            {
            }

            public void AddMethod(LightweightMethodStub methodStub)
            {
                this.methodStubs.Add(methodStub);
            }

            public ImmutableArray<LightweightMethodStub> GetMethodStubs()
            {
                return ImmutableArray.CreateRange(this.methodStubs);
            }
        }
    }
}
