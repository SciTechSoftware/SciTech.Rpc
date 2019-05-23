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

using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using SciTech.Rpc.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace SciTech.Rpc.Pipelines.Server.Internal
{
    internal interface IPipelinesMethodBinder
    {
        void AddMethod(PipelinesMethodStub methodStub);
    }

    internal interface IPipelinesServiceStubBuilder
    {
        PipelinesServerServiceDefinition Build(RpcPipelinesServer server);
    }

    public class PipelinesServerServiceDefinition
    {
        internal PipelinesServerServiceDefinition(string serviceName, ImmutableArray<PipelinesMethodStub> methodStubs)
        {
            this.ServiceName = serviceName;
            this.MethodStubs = methodStubs;

        }

        public string ServiceName { get; }

        internal ImmutableArray<PipelinesMethodStub> MethodStubs { get; }
    }

    /// <summary>
    /// The PipelinesServiceStubBuilder builds a type implementing server side stubs for an RPC service defined by an RpcService
    /// interface. The service interface must be tagged with the <see cref="RpcServiceAttribute"/> attribute.
    /// Note, this class will only generate an implementation for the declared members of the service, nothing
    /// is generated for inherited members.
    /// </summary>
    internal class PipelinesServiceStubBuilder<TService>
        : RpcServiceStubBuilder<TService, IPipelinesMethodBinder>, IPipelinesServiceStubBuilder
        where TService : class
    {
        public PipelinesServiceStubBuilder(IRpcSerializer serializer) :
            this(RpcBuilderUtil.GetServiceInfoFromType(typeof(TService)), serializer)
        {
        }

        public PipelinesServiceStubBuilder(RpcServiceInfo serviceInfo, IRpcSerializer serializer) : base(serviceInfo, serializer)
        {

        }

        public PipelinesServerServiceDefinition Build(RpcPipelinesServer server)
        {
            var binder = new Binder();
            this.GenerateOperationHandlers(server, binder);

            return new PipelinesServerServiceDefinition(this.ServiceInfo.FullName, binder.GetMethodStubs());
        }

        protected override void AddEventHandlerDefinition<TEventArgs>(
            RpcEventInfo eventInfo,
            Func<RpcObjectRequest, IServiceProvider?, IRpcAsyncStreamWriter<TEventArgs>, IRpcCallContext, ValueTask> beginEventProducer,
            RpcStub<TService> serviceStub,
            IPipelinesMethodBinder binder)
        {
            var beginEventProducerName = $"{eventInfo.FullServiceName}.Begin{eventInfo.Name}";

            var methodStub = new PipelinesMethodStub<RpcObjectRequest, TEventArgs>(beginEventProducerName, beginEventProducer, this.serializer, null);

            binder.AddMethod(methodStub);
        }

        protected override void AddGenericAsyncMethodImpl<TRequest, TReturn, TResponseReturn>(
            Func<TService, TRequest, Task<TReturn>> serviceCaller,
            Func<TReturn, TResponseReturn>? responseConverter,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            IPipelinesMethodBinder binder)
        {
            var serializer = this.serializer;

            ValueTask<RpcResponse<TResponseReturn>> HandleRequest(TRequest request, IServiceProvider? serviceProvider, PipelinesCallContext context) 
                => serviceStub.CallAsyncMethod(request, serviceProvider, context, serviceCaller, responseConverter, faultHandler, serializer);

            var methodStub = new PipelinesMethodStub<TRequest, RpcResponse<TResponseReturn>>(operationInfo.FullName, HandleRequest, serializer, faultHandler);

            binder.AddMethod(methodStub);
        }

        protected override void AddGenericBlockingMethodImpl<TRequest, TReturn, TResponseReturn>(
            Func<TService, TRequest, TReturn> serviceCaller,
            Func<TReturn, TResponseReturn>? responseConverter,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            IPipelinesMethodBinder binder)
        {
            var serializer = this.serializer;
            ValueTask<RpcResponse<TResponseReturn>> HandleRequest(TRequest request, IServiceProvider? serviceProvider, PipelinesCallContext context) 
                => serviceStub.CallBlockingMethod(request, serviceProvider, context, serviceCaller, responseConverter, faultHandler, serializer);

            var methodStub = new PipelinesMethodStub<TRequest, RpcResponse<TResponseReturn>>(operationInfo.FullName, HandleRequest, serializer, faultHandler);
            binder.AddMethod(methodStub);
        }

        protected override void AddGenericVoidAsyncMethodImpl<TRequest>(
            Func<TService, TRequest, Task> serviceCaller,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            IPipelinesMethodBinder binder)
        {
            var serializer = this.serializer;
            ValueTask<RpcResponse> HandleRequest(TRequest request, IServiceProvider? serviceProvider, PipelinesCallContext context)
                => serviceStub.CallVoidAsyncMethod(request, serviceProvider, context, serviceCaller, faultHandler, serializer);

            var methodStub = new PipelinesMethodStub<TRequest, RpcResponse>(operationInfo.FullName, HandleRequest, serializer, faultHandler);
            binder.AddMethod(methodStub);
        }

        protected override void AddGenericVoidBlockingMethodImpl<TRequest>(
            Action<TService, TRequest> serviceCaller,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            IPipelinesMethodBinder binder)
        {
            var serializer = this.serializer;

            ValueTask<RpcResponse> HandleRequest(TRequest request, IServiceProvider? serviceProvider, PipelinesCallContext context)
                => serviceStub.CallVoidBlockingMethod(request, serviceProvider, context, serviceCaller, faultHandler, serializer);

            var methodStub = new PipelinesMethodStub<TRequest, RpcResponse>(operationInfo.FullName, HandleRequest, serializer, faultHandler);
            binder.AddMethod(methodStub);
        }

        protected override RpcStub<TService> CreateServiceStub(IRpcServerImpl server)
        {
            return new RpcStub<TService>(server);
        }

        private class Binder : IPipelinesMethodBinder
        {
            private List<PipelinesMethodStub> methodStubs = new List<PipelinesMethodStub>();

            public Binder()
            {
            }

            public void AddMethod(PipelinesMethodStub methodStub)
            {
                this.methodStubs.Add(methodStub);
            }

            public ImmutableArray<PipelinesMethodStub> GetMethodStubs()
            {
                return ImmutableArray.CreateRange(this.methodStubs);
            }
        }
    }
}
