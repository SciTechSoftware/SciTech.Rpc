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

using SciTech.Rpc.Internal;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Server.Internal
{
    /// <summary>
    /// The RpcServiceStubBuilder builds a type implementing server side stubs for an  RPC service defined by an RpcService
    /// interface. The service interface must be tagged with the <see cref="RpcServiceAttribute"/> attribute.
    /// Note, this class will only generate an implementation for the declared members of the interface, nothing
    /// is generated for inherited members.
    /// </summary>
    public abstract class RpcServiceStubBuilder<TService, TMethodBinder> where TService : class
    {
        private HashSet<string> addedOperations = new HashSet<string>();

        private IReadOnlyList<IRpcServerExceptionConverter> serviceErrorGenerators;

        private RpcStub<TService>? serviceStub;

        protected RpcServiceStubBuilder(RpcServiceOptions<TService>? options) :
            this(RpcBuilderUtil.GetServiceInfoFromType(typeof(TService)), options)
        {
        }

        protected RpcServiceStubBuilder(RpcServiceInfo serviceInfo, RpcServiceOptions<TService>? options)
        {
            this.ServiceInfo = serviceInfo;
            this.Options = options;

            var faultAttributes = serviceInfo.Type.GetCustomAttributes(typeof(RpcFaultAttribute));
            this.serviceErrorGenerators = RetrieveErrorGenerators(faultAttributes);
        }

        protected RpcServiceOptions<TService>? Options { get; }

        protected RpcServiceInfo ServiceInfo { get; }

        /// <summary>
        /// Generates the RPC method definitions and stub handlers and adds them to the provided methodBinder.
        /// </summary>
        /// <returns></returns>
        public RpcStub<TService> GenerateOperationHandlers(IRpcServerImpl server, TMethodBinder methodBinder)
        {
            this.serviceStub = this.CreateServiceStub(server);
            var handledMembers = new HashSet<MemberInfo>();

            //bool anyEventAdded = false;
            var events = this.ServiceInfo.Type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var eventInfo in events)
            {
                var rpcEventInfo = RpcBuilderUtil.GetEventInfoFromEvent(this.ServiceInfo, eventInfo);
                this.CheckEvent(rpcEventInfo);
                this.AddEventHandler(this.serviceStub, rpcEventInfo, methodBinder);

                handledMembers.Add(rpcEventInfo.Event.AddMethod);
                handledMembers.Add(rpcEventInfo.Event.RemoveMethod);
                //anyEventAdded = true;
            }

            var properties = this.ServiceInfo.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var propertyInfo in properties)
            {
                var rpcPropertyInfo = RpcBuilderUtil.GetPropertyInfoFromProperty(this.ServiceInfo, propertyInfo);
                this.CheckOperation(rpcPropertyInfo.FullName);

                if (propertyInfo.GetMethod != null)
                {
                    this.AddGetProperty(this.serviceStub, rpcPropertyInfo, methodBinder);
                    handledMembers.Add(propertyInfo.GetMethod);
                }

                if (propertyInfo.SetMethod != null)
                {
                    CheckPropertySet(rpcPropertyInfo);

                    this.AddSetProperty(this.serviceStub, rpcPropertyInfo, methodBinder);
                    handledMembers.Add(propertyInfo.SetMethod);
                }
            }

            foreach (var method in this.ServiceInfo.Type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (handledMembers.Add(method))
                {
                    var opInfo = RpcBuilderUtil.GetOperationInfoFromMethod(this.ServiceInfo, method);
                    this.CheckMethod(opInfo);
                    switch (opInfo.MethodType)
                    {
                        case RpcMethodType.Unary:
                            this.AddUnaryMethod(this.serviceStub, opInfo, methodBinder);
                            break;

                    }
                }
            }

            return this.serviceStub;
        }

        internal void AddEventHandler(RpcStub<TService> serviceStub, RpcEventInfo eventInfo, TMethodBinder binder)
        {
            if (eventInfo.Event.EventHandlerType.Equals(typeof(EventHandler)))
            {
                this.AddPlainEventHandler(serviceStub, eventInfo, binder);
                return;
            }
            // TODO: Cache.
            var addEventHandlerDelegate = (Action<RpcStub<TService>, RpcEventInfo, TMethodBinder>)this.GetType()
                .GetMethod(nameof(this.AddGenericEventHandler), BindingFlags.Instance | BindingFlags.NonPublic)
                .MakeGenericMethod(eventInfo.EventArgsType)
                .CreateDelegate(typeof(Action<RpcStub<TService>, RpcEventInfo, TMethodBinder>), this);

            addEventHandlerDelegate(serviceStub, eventInfo, binder);

        }

        internal void AddGenericEventHandler<TEventArgs>(RpcStub<TService> serviceStub, RpcEventInfo eventInfo, TMethodBinder binder) where TEventArgs : class
        {
            var baseName = eventInfo.Name;
            var beginEventProducerName = $"Begin{baseName}";

            var serviceParameter = Expression.Parameter(typeof(TService));
            var delegateParameter = Expression.Parameter(typeof(Delegate));

            var eventHandlerParameter = Expression.Parameter(eventInfo.Event.EventHandlerType);

            var castDelegateExpression = Expression.Convert(delegateParameter, eventInfo.Event.EventHandlerType);

            var addHandlerExpression = Expression.Call(serviceParameter, eventInfo.Event.AddMethod, castDelegateExpression);
            var addHandlerAction = Expression.Lambda<Action<TService, Delegate>>(addHandlerExpression, serviceParameter, delegateParameter).Compile();

            var removeHandlerExpression = Expression.Call(serviceParameter, eventInfo.Event.RemoveMethod, castDelegateExpression);
            var removeHandlerAction = Expression.Lambda<Action<TService, Delegate>>(removeHandlerExpression, serviceParameter, delegateParameter).Compile();

            ValueTask LocalBeginEventProducer(RpcObjectRequest request, IServiceProvider? serviceProvider, IRpcAsyncStreamWriter<TEventArgs> responseStream, IRpcCallContext context)
            {
                var eventProducer = new GenericEventHandlerProducer<TEventArgs>(responseStream, addHandlerAction, removeHandlerAction, context.CancellationToken);
                return serviceStub.BeginEventProducer(request, serviceProvider, eventProducer, context);
            }

            this.AddEventHandlerDefinition<TEventArgs>(eventInfo, LocalBeginEventProducer, serviceStub, binder);
        }

        protected static Func<TService, TRequest, TResult> GenerateBlockingUnaryMethodHandler<TRequest, TResult>(RpcOperationInfo operationInfo)
        {
            var requestParameter = Expression.Parameter(typeof(TRequest));

            List<Expression> parameters = new List<Expression>();
            for (int paramIndex = 1; paramIndex < operationInfo.RequestTypeCtorArgTypes.Length; paramIndex++)
            {
                var parameterType = operationInfo.RequestTypeCtorArgTypes[paramIndex];
                parameters.Add(Expression.Field(requestParameter, typeof(TRequest), $"Value{paramIndex}"));
            }

            var serviceParameter = Expression.Parameter(typeof(TService));

            var invocation = Expression.Call(serviceParameter, operationInfo.Method, parameters);
            var func = Expression.Lambda<Func<TService, TRequest, TResult>>(invocation, false, serviceParameter, requestParameter).Compile();


            return func;
        }
        ///// <summary>
        ///// Generates an expression function that calls a service implementation method that returns an RPC service instance (i.e. an
        ///// interface marked with an <see cref="RpcServiceAttribute"/>).
        ///// </summary>
        ///// <remarks>
        ///// Assuming that the service interface is defined as:
        ///// <code>
        ///// [RpcService]
        ///// public interface IImplicitServiceProviderService
        ///// {
        /////     ISimpleService GetSimpleService( int serviceId );
        ///// } 
        ///// </code>
        ///// 
        ///// This method will generate a function similar to (for the GetSimpleService stub):
        ///// <code>
        ///// (IImplicitServiceProviderService service, IRpcServicePublisher servicePublisher, RpcObjectRequest<int> request) 
        /////     => CreateServiceResponse(servicePublisher, service.GetSimpleService( request.Value1 ) );
        ///// </code>
        ///// </remarks>
        ///// <typeparam name="TRequest"></typeparam>
        ///// <typeparam name="TResult"></typeparam>
        ///// <param name="operationInfo"></param>
        ///// <returns></returns>
        //protected static Func<TService, IRpcServicePublisher, TRequest, RpcResponse<RpcObjectRef<TServiceResult>>>
        //    GenerateBlockingUnaryServiceMethodHandler<TRequest, TServiceResult>(RpcOperationInfo operationInfo)
        //    where TServiceResult : class
        //{
        //    var requestParameter = Expression.Parameter(typeof(TRequest));
        //    List<Expression> parameters = new List<Expression>();
        //    for (int paramIndex = 1; paramIndex < operationInfo.RequestTypeCtorArgTypes.Length; paramIndex++)
        //    {
        //        var parameterType = operationInfo.RequestTypeCtorArgTypes[paramIndex];
        //        parameters.Add(Expression.Field(requestParameter, typeof(TRequest), $"Value{paramIndex}"));
        //    }
        //    var serviceParameter = Expression.Parameter(typeof(TService));
        //    var publisherParameter = Expression.Parameter(typeof(IRpcServicePublisher));
        //    var serviceCall = Expression.Call(serviceParameter, operationInfo.Method, parameters);
        //    var createServiceResponseMethodDef = typeof(RpcStub).GetMethod(nameof(RpcStub.CreateServiceResponse), BindingFlags.Static | BindingFlags.NonPublic);
        //    var createServiceResponseMethod = createServiceResponseMethodDef.MakeGenericMethod(typeof(TServiceResult));
        //    var createResponseCall = Expression.Call(null, createServiceResponseMethod, publisherParameter, serviceCall);
        //    var func = Expression.Lambda<Func<TService, IRpcServicePublisher, TRequest, RpcResponse<RpcObjectRef<TServiceResult>>>>(
        //        createResponseCall, false,
        //        serviceParameter,
        //        publisherParameter,
        //        requestParameter).Compile();
        //    return func;
        //}

        protected static Func<TService, TRequest, Task<TResponse>> GenerateUnaryMethodHandler<TRequest, TResponse>(RpcOperationInfo operationInfo)
            where TRequest : class
        {
            var requestParameter = Expression.Parameter(typeof(TRequest));

            List<Expression> parameters = new List<Expression>();
            for (int paramIndex = 1; paramIndex < operationInfo.RequestTypeCtorArgTypes.Length; paramIndex++)
            {
                var parameterType = operationInfo.RequestTypeCtorArgTypes[paramIndex];
                parameters.Add(Expression.Field(requestParameter, typeof(TRequest), $"Value{paramIndex}"));
            }

            var serviceParameter = Expression.Parameter(typeof(TService));

            var invocation = Expression.Call(serviceParameter, operationInfo.Method, parameters);
            Expression<Func<TService, TRequest, Task<TResponse>>> expression = Expression.Lambda<Func<TService, TRequest, Task<TResponse>>>(invocation, false, serviceParameter, requestParameter);

            var func = expression.Compile();
            return func;
        }

        protected static Func<TService, TRequest, Task<TResult>> GenerateUnaryServiceMethodHandler<TRequest, TResult>(RpcOperationInfo operationInfo)
            where TRequest : class
        {
            var requestParameter = Expression.Parameter(typeof(TRequest));

            List<Expression> parameters = new List<Expression>();
            for (int paramIndex = 1; paramIndex < operationInfo.RequestTypeCtorArgTypes.Length; paramIndex++)
            {
                var parameterType = operationInfo.RequestTypeCtorArgTypes[paramIndex];
                parameters.Add(Expression.Field(requestParameter, typeof(TRequest), $"Value{paramIndex}"));
            }

            var serviceParameter = Expression.Parameter(typeof(TService));

            var invocation = Expression.Call(serviceParameter, operationInfo.Method, parameters);
            Expression<Func<TService, TRequest, Task<TResult>>> expression = Expression.Lambda<Func<TService, TRequest, Task<TResult>>>(invocation, false, serviceParameter, requestParameter);

            var func = expression.Compile();
            return func;
        }

        protected abstract void AddEventHandlerDefinition<TEventArgs>(
            RpcEventInfo eventInfo,
            Func<RpcObjectRequest, IServiceProvider?, IRpcAsyncStreamWriter<TEventArgs>, IRpcCallContext, ValueTask> beginEventProducer,
            RpcStub<TService> serviceStub,
            TMethodBinder binder)
            where TEventArgs : class;

        protected abstract void AddGenericAsyncMethodImpl<TRequest, TReturn, TResponseReturn>(
            Func<TService, TRequest, Task<TReturn>> serviceCaller,
            Func<TReturn, TResponseReturn>? responseConverter,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            TMethodBinder binder)
            where TRequest : class, IObjectRequest;

        protected void AddGenericBlockingMethod<TRequest, TReturn, TResponseReturn>(RpcStub<TService> serviceStub, RpcOperationInfo opInfo, TMethodBinder binder)
            where TRequest : class, IObjectRequest
        {
            Func<TReturn, TResponseReturn>? responseCreator = GetResponseCreator<TReturn, TResponseReturn>(opInfo);
            RpcServerFaultHandler faultHandler = this.CreateFaultHandler(opInfo);

            if (opInfo.IsAsync)
            {
                var serviceCaller = GenerateUnaryMethodHandler<TRequest, TReturn>(opInfo);
                this.AddGenericAsyncMethodImpl(serviceCaller, responseCreator, faultHandler, serviceStub, opInfo, binder);
            }
            else
            {
                var serviceCaller = GenerateBlockingUnaryMethodHandler<TRequest, TReturn>(opInfo);
                this.AddGenericBlockingMethodImpl(serviceCaller, responseCreator, faultHandler, serviceStub, opInfo, binder);
            }
        }

        protected abstract void AddGenericBlockingMethodImpl<TRequest, TReturn, TResponseReturn>(
            Func<TService, TRequest, TReturn> serviceCaller,
            Func<TReturn, TResponseReturn>? responseConverter,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            TMethodBinder binder)
            where TRequest : class, IObjectRequest;

        protected abstract void AddGenericVoidAsyncMethodImpl<TRequest>(
            Func<TService, TRequest, Task> serviceCaller,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            TMethodBinder binder)
            where TRequest : class, IObjectRequest;

        protected abstract void AddGenericVoidBlockingMethodImpl<TRequest>(
            Action<TService, TRequest> serviceCaller,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            TMethodBinder binder)
            where TRequest : class, IObjectRequest;

        private static void CheckPropertySet(RpcPropertyInfo propertyInfo)
        {
            if (propertyInfo.PropertyTypeKind != ServiceOperationReturnKind.Standard)
            {
                throw new RpcDefinitionException($"Type {propertyInfo.Property.PropertyType} is not valid for RPC service property '{propertyInfo.Name}'.");
            }
        }

        private static Action<TService, TRequest> GenerateVoidBlockingUnaryMethodHandler<TRequest>(RpcOperationInfo operationInfo)
        {
            var requestParameter = Expression.Parameter(typeof(TRequest));

            List<Expression> parameters = new List<Expression>();
            for (int paramIndex = 1; paramIndex < operationInfo.RequestTypeCtorArgTypes.Length; paramIndex++)
            {
                var parameterType = operationInfo.RequestTypeCtorArgTypes[paramIndex];
                parameters.Add(Expression.Field(requestParameter, typeof(TRequest), $"Value{paramIndex}"));
            }

            var serviceParameter = Expression.Parameter(typeof(TService));

            var invocation = Expression.Call(serviceParameter, operationInfo.Method, parameters);
            var func = Expression.Lambda<Action<TService, TRequest>>(invocation, false, serviceParameter, requestParameter).Compile();
            return func;
        }

        private static Func<TService, TRequest, Task> GenerateVoidUnaryMethodHandler<TRequest>(RpcOperationInfo operationInfo)
        {
            var requestParameter = Expression.Parameter(typeof(TRequest));

            List<Expression> parameters = new List<Expression>();
            for (int paramIndex = 1; paramIndex < operationInfo.RequestTypeCtorArgTypes.Length; paramIndex++)
            {
                var parameterType = operationInfo.RequestTypeCtorArgTypes[paramIndex];
                parameters.Add(Expression.Field(requestParameter, typeof(TRequest), $"Value{paramIndex}"));
            }

            var serviceParameter = Expression.Parameter(typeof(TService));

            var invocation = Expression.Call(serviceParameter, operationInfo.Method, parameters);
            var func = Expression.Lambda<Func<TService, TRequest, Task>>(invocation, false, serviceParameter, requestParameter).Compile();
            return func;
        }

        private static List<IRpcServerExceptionConverter> RetrieveErrorGenerators(IEnumerable<Attribute> faultAttributes)
        {
            var errorGenerators = new List<IRpcServerExceptionConverter>();
            foreach (RpcFaultAttribute faultAttribute in faultAttributes)
            {

                if (faultAttribute.FaultType != null)
                {
                    var rpcErrorGenerator = (IRpcServerExceptionConverter)typeof(RpcFaultExceptionConverter<>)
                        .MakeGenericType(faultAttribute.FaultType)
                        .GetField(nameof(RpcFaultExceptionConverter<object>.Default), BindingFlags.Static | BindingFlags.Public)
                        .GetValue(null);
                    errorGenerators.Add(rpcErrorGenerator);
                }
                else
                {
                    var exceptionConverter = new RpcFaultExceptionConverter(faultAttribute.FaultCode);
                    errorGenerators.Add(exceptionConverter);
                }
            }

            return errorGenerators;
        }

        private void AddGenericVoidUnaryMethod<TRequest>(RpcStub<TService> serviceStub, RpcOperationInfo opInfo, TMethodBinder binder)
            where TRequest : class, IObjectRequest
        {
            RpcServerFaultHandler faultHandler = this.CreateFaultHandler(opInfo);

            if (opInfo.IsAsync)
            {
                var serviceCaller = GenerateVoidUnaryMethodHandler<TRequest>(opInfo);
                this.AddGenericVoidAsyncMethodImpl(serviceCaller, faultHandler, serviceStub, opInfo, binder);
            }
            else
            {
                var serviceCaller = GenerateVoidBlockingUnaryMethodHandler<TRequest>(opInfo);
                this.AddGenericVoidBlockingMethodImpl(serviceCaller, faultHandler, serviceStub, opInfo, binder);
            }
        }

        private void AddGetProperty(RpcStub<TService> serviceStub, RpcPropertyInfo rpcPropertyInfo, TMethodBinder binder)
        {
            var propertyInfo = rpcPropertyInfo.Property;

            this.AddUnaryMethod(serviceStub, new RpcOperationInfo
            (
                service: rpcPropertyInfo.Service,
                name: $"Get{propertyInfo.Name}",
                method: propertyInfo.GetMethod,
                requestType: typeof(RpcObjectRequest),
                requestTypeCtorArgTypes: ImmutableArray.Create(typeof(RpcObjectId)),
                methodType: RpcMethodType.Unary,
                isAsync: false,
                parametersCount: 0,
                responseType: typeof(RpcResponse<>).MakeGenericType(rpcPropertyInfo.ResponseReturnType),
                returnType: propertyInfo.PropertyType,
                responseReturnType: rpcPropertyInfo.ResponseReturnType,
                returnKind: rpcPropertyInfo.PropertyTypeKind
            ),
            binder
            );
        }

        private void AddPlainEventHandler(RpcStub<TService> serviceStub, RpcEventInfo eventInfo, TMethodBinder binder)
        {
            var baseName = eventInfo.Name;
            var beginEventProducerName = $"Begin{baseName}";

            var serviceParameter = Expression.Parameter(typeof(TService));
            var eventHandlerParameter = Expression.Parameter(eventInfo.Event.EventHandlerType);
            var delegateParameter = Expression.Parameter(typeof(Delegate));

            var castDelegateExpression = Expression.Convert(delegateParameter, eventInfo.Event.EventHandlerType);

            var addHandlerExpression = Expression.Call(serviceParameter, eventInfo.Event.AddMethod, castDelegateExpression);
            var addHandlerAction = Expression.Lambda<Action<TService, Delegate>>(addHandlerExpression, serviceParameter, delegateParameter).Compile();

            var removeHandlerExpression = Expression.Call(serviceParameter, eventInfo.Event.RemoveMethod, castDelegateExpression);
            var removeHandlerAction = Expression.Lambda<Action<TService, Delegate>>(removeHandlerExpression, serviceParameter, delegateParameter).Compile();

            ValueTask LocalBeginEventProducer(RpcObjectRequest request, IServiceProvider? serviceProvider, IRpcAsyncStreamWriter<EventArgs> responseStream, IRpcCallContext context)
            {
                var eventProducer = new PlainEventHandlerProducer(responseStream, addHandlerAction, removeHandlerAction, context.CancellationToken);
                return serviceStub.BeginEventProducer(request, serviceProvider, eventProducer, context);
            }

            this.AddEventHandlerDefinition<EventArgs>(eventInfo, LocalBeginEventProducer, serviceStub, binder);
        }

        private void AddSetProperty(RpcStub<TService> serviceStub, RpcPropertyInfo rpcPropertyInfo, TMethodBinder binder)
        {
            var propertyInfo = rpcPropertyInfo.Property;

            this.AddUnaryMethod(serviceStub, new RpcOperationInfo
            (
                service: rpcPropertyInfo.Service,
                name: $"Set{propertyInfo.Name}",
                method: propertyInfo.SetMethod,
                requestType: typeof(RpcObjectRequest<>).MakeGenericType(propertyInfo.PropertyType),
                requestTypeCtorArgTypes: ImmutableArray.Create(typeof(RpcObjectId), propertyInfo.PropertyType),
                methodType: RpcMethodType.Unary,
                isAsync: false,
                parametersCount: 1,
                responseType: typeof(RpcResponse),
                returnType: typeof(void),
                responseReturnType: typeof(void),
                returnKind: rpcPropertyInfo.PropertyTypeKind
            ),
            binder
            );
        }

        private void AddUnaryMethod(RpcStub<TService> serviceStub, RpcOperationInfo opInfo, TMethodBinder binder)
        {
            if (!opInfo.ReturnType.Equals(typeof(void)))
            {
                // TODO: Cache.                
                var addUnaryMethodDelegate = (Action<RpcStub<TService>, RpcOperationInfo, TMethodBinder>)
                    typeof(RpcServiceStubBuilder<TService, TMethodBinder>)
                    .GetMethod(nameof(this.AddGenericBlockingMethod), BindingFlags.Instance | BindingFlags.NonPublic)
                    .MakeGenericMethod(opInfo.RequestType, opInfo.ReturnType, opInfo.ResponseReturnType)
                    .CreateDelegate(typeof(Action<RpcStub<TService>, RpcOperationInfo, TMethodBinder>), this);

                addUnaryMethodDelegate(serviceStub, opInfo, binder);
            }
            else
            {
                // TODO: Cache.
                var addUnaryMethodDelegate = (Action<RpcStub<TService>, RpcOperationInfo, TMethodBinder>)
                    typeof(RpcServiceStubBuilder<TService, TMethodBinder>)
                    .GetMethod(nameof(this.AddGenericVoidUnaryMethod), BindingFlags.Instance | BindingFlags.NonPublic)
                    .MakeGenericMethod(opInfo.RequestType)
                    .CreateDelegate(typeof(Action<RpcStub<TService>, RpcOperationInfo, TMethodBinder>), this);

                addUnaryMethodDelegate(serviceStub, opInfo, binder);

            }
        }

        private void CheckEvent(RpcEventInfo eventInfo)
        {
            this.CheckOperation(eventInfo.FullName);
        }

        private void CheckMethod(RpcOperationInfo methodInfo)
        {
            //if (methodInfo.ReturnKind != ServiceOperationReturnKind.Standard)
            //{
            //    throw new RpcDefinitionException($"Type {methodInfo.ReturnType} is not a valid RPC service return type for method '{methodInfo.Name}'.");
            //}

            this.CheckOperation(methodInfo.FullName);
        }

        private void CheckOperation(string fullName)
        {
            if (!this.addedOperations.Add(fullName))
            {
                throw new RpcDefinitionException($"Operation with name {fullName} has already been added.");
            }
        }

        /// <summary>
        /// TODO: To support AOT compilation in the future, the fault handler should probably be returned
        /// as an expression that can be pre-compiled.
        /// </summary>
        /// <param name="opInfo"></param>
        /// <returns></returns>
        private RpcServerFaultHandler CreateFaultHandler(RpcOperationInfo opInfo)
        {
            var faultAttributes = opInfo.Method.GetCustomAttributes(typeof(RpcFaultAttribute));
            List<IRpcServerExceptionConverter> exceptionConverters = RetrieveErrorGenerators(faultAttributes);

            if (exceptionConverters.Count > 0 || this.serviceErrorGenerators.Count > 0)
            {
                exceptionConverters.AddRange(this.serviceErrorGenerators);
                return new RpcServerFaultHandler(exceptionConverters);
            }

            return RpcServerFaultHandler.Default;
        }

        private RpcStub<TService> CreateServiceStub(IRpcServerImpl server)
        {
            return new RpcStub<TService>(server, this.Options);
        }

        private Func<TReturn, TResponseReturn>? GetResponseCreator<TReturn, TResponseReturn>(RpcOperationInfo opInfo)
        {
            Func<TReturn, TResponseReturn>? responseCreator = null;
            switch (opInfo.ReturnKind)
            {
                case ServiceOperationReturnKind.Service:
                    {
                        var method = typeof(RpcStub)
                            .GetMethod(nameof(RpcStub.ConvertServiceResponse))
                            .MakeGenericMethod(typeof(TReturn));

                        var func = method.CreateDelegate(typeof(Func<TReturn, TResponseReturn>), this.serviceStub);

                        responseCreator = (Func<TReturn, TResponseReturn>)func;

                        break;
                    }

                case ServiceOperationReturnKind.ServiceArray:
                    {
                        var elementType = typeof(TReturn).GetElementType();
                        var method = typeof(RpcStub)
                            .GetMethod(nameof(RpcStub.ConvertServiceArrayResponse))
                            .MakeGenericMethod(elementType);

                        var func = method.CreateDelegate(typeof(Func<TReturn, TResponseReturn>), this.serviceStub);

                        responseCreator = (Func<TReturn, TResponseReturn>)func;

                        break;
                    }
                case ServiceOperationReturnKind.ServiceRef:
                    {
                        var method = typeof(RpcStub)
                            .GetMethod(nameof(RpcStub.ConvertServiceRefResponse))
                            .MakeGenericMethod(typeof(TReturn));

                        var func = method.CreateDelegate(typeof(Func<TReturn, TResponseReturn>));

                        responseCreator = (Func<TReturn, TResponseReturn>)func;

                        break;
                    }
                case ServiceOperationReturnKind.ServiceRefArray:
                    {
                        var elementType = typeof(TReturn).GetElementType();

                        var method = typeof(RpcStub)
                            .GetMethod(nameof(RpcStub.ConvertServiceRefArrayResponse))
                            .MakeGenericMethod(elementType);

                        var func = method.CreateDelegate(typeof(Func<TReturn, TResponseReturn>));

                        responseCreator = (Func<TReturn, TResponseReturn>)func;

                        break;
                    }
                default:
                    Debug.Assert(typeof(TResponseReturn).IsAssignableFrom(typeof(TReturn)));
                    break;
            }

            return responseCreator;
        }

        private class GenericEventHandlerProducer<TEventArgs> : EventProducer<TService, TEventArgs> where TEventArgs : class
        {
            private Action<TService, EventHandler<TEventArgs>> addHandlerAction;

            private Action<TService, EventHandler<TEventArgs>> removeHandlerAction;

            internal GenericEventHandlerProducer(
                IRpcAsyncStreamWriter<TEventArgs> responseStream,
                Action<TService, EventHandler<TEventArgs>> addHandlerAction,
                Action<TService, EventHandler<TEventArgs>> removeHandlerAction,
                CancellationToken cancellationToken) : base(responseStream, cancellationToken)
            {
                this.addHandlerAction = addHandlerAction;
                this.removeHandlerAction = removeHandlerAction;

            }

            protected override async Task RunImpl(TService service)
            {
                this.addHandlerAction(service, this.Handler);

                try
                {
                    await this.RunReceiveLoop().ContextFree();
                }
                finally
                {
                    if (RpcStubOptions.TestDelayEventHandlers)
                    {
                        await Task.Delay(100).ConfigureAwait(false);
                    }

                    this.removeHandlerAction(service, this.Handler);
                }
            }

            private void Handler(object s, TEventArgs e)
            {
                this.HandleEvent(e);
            }
        }

        private class PlainEventHandlerProducer : EventProducer<TService, EventArgs>
        {
            private Action<TService, EventHandler> addHandlerAction;

            private Action<TService, EventHandler> removeHandlerAction;

            internal PlainEventHandlerProducer(
                IRpcAsyncStreamWriter<EventArgs> responseStream,
                Action<TService, EventHandler> addHandlerAction,
                Action<TService, EventHandler> removeHandlerAction,
                CancellationToken cancellationToken) : base(responseStream, cancellationToken)
            {
                this.addHandlerAction = addHandlerAction;
                this.removeHandlerAction = removeHandlerAction;

            }

            protected override async Task RunImpl(TService service)
            {
                this.addHandlerAction(service, this.Handler);

                try
                {
                    await this.RunReceiveLoop().ContextFree();
                }
                finally
                {
                    if (RpcStubOptions.TestDelayEventHandlers)
                    {
                        await Task.Delay(100).ConfigureAwait(false);
                    }

                    this.removeHandlerAction(service, this.Handler);
                }
            }

            private void Handler(object s, EventArgs e)
            {
                this.HandleEvent(e);
            }
        }
    }
}