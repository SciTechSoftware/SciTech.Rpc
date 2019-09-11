#region Copyright notice and license
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
using System.Linq;
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
#pragma warning disable CA1062 // Validate arguments of public methods
    public abstract class RpcServiceStubBuilder<TService, TMethodBinder> where TService : class
    {

        private HashSet<string> addedOperations = new HashSet<string>();

        private IReadOnlyList<IRpcServerExceptionConverter> serviceErrorGenerators;

        private RpcStub<TService>? serviceStub;

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

            foreach (var memberInfo in RpcBuilderUtil.EnumOperationHandlers(this.ServiceInfo, true))
            {
                if (memberInfo is RpcEventInfo eventInfo)
                {
                    this.AddEventHandler(this.serviceStub, eventInfo, methodBinder);
                }
                else if (memberInfo is RpcOperationInfo opInfo)
                {
                    switch (opInfo.MethodType)
                    {
                        case RpcMethodType.Unary:
                            this.CheckMethod(opInfo);
                            this.AddUnaryMethod(this.serviceStub, opInfo, methodBinder);
                            break;
                        case RpcMethodType.ServerStreaming:
                            this.CheckMethod(opInfo);
                            this.AddServerStreamingMethod(this.serviceStub, opInfo, methodBinder);
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                }
                else
                {
                    throw new NotImplementedException();
                }
            }

            return this.serviceStub;
        }

        internal void AddEventHandler(RpcStub<TService> serviceStub, RpcEventInfo eventInfo, TMethodBinder binder)
        {
            if (typeof(EventHandler).Equals(eventInfo.Event.EventHandlerType))
            {
                this.AddPlainEventHandler(serviceStub, eventInfo, binder);
                return;
            }

            // TODO: Cache.
            var addEventHandlerDelegate = (Action<RpcStub<TService>, RpcEventInfo, TMethodBinder>)
                GetBuilderMethod(nameof(this.AddGenericEventHandler))
                .MakeGenericMethod(eventInfo.EventArgsType)
                .CreateDelegate(typeof(Action<RpcStub<TService>, RpcEventInfo, TMethodBinder>), this);

            addEventHandlerDelegate(serviceStub, eventInfo, binder);

        }

        internal void AddGenericEventHandler<TEventArgs>(RpcStub<TService> serviceStub, RpcEventInfo eventInfo, TMethodBinder binder) where TEventArgs : class
        {
            var serviceParameter = Expression.Parameter(typeof(TService));
            var delegateParameter = Expression.Parameter(typeof(Delegate));

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

        protected static Func<TService, TRequest, CancellationToken, TResult> GenerateBlockingUnaryMethodHandler<TRequest, TResult>(RpcOperationInfo operationInfo)
        {
            var requestParameter = Expression.Parameter(typeof(TRequest));
            var cancellationTokenParameter = Expression.Parameter(typeof(CancellationToken));

            List<Expression> parameterExpressions = GetParameterExpressions<TRequest>(operationInfo, requestParameter, cancellationTokenParameter);

            var serviceParameter = Expression.Parameter(typeof(TService));

            var invocation = Expression.Call(serviceParameter, operationInfo.Method, parameterExpressions);
            var func = Expression.Lambda<Func<TService, TRequest, CancellationToken, TResult>>(
                invocation, false, serviceParameter, requestParameter, cancellationTokenParameter).Compile();

            return func;
        }

        protected static Func<TService, TRequest, CancellationToken, Task<TResponse>> GenerateUnaryMethodHandler<TRequest, TResponse>(RpcOperationInfo operationInfo)
            where TRequest : class
        {
            var requestParameter = Expression.Parameter(typeof(TRequest));
            var cancellationTokenParameter = Expression.Parameter(typeof(CancellationToken));

            List<Expression> parameterExpressions = GetParameterExpressions<TRequest>(operationInfo, requestParameter, cancellationTokenParameter);

            var serviceParameter = Expression.Parameter(typeof(TService));

            var invocation = Expression.Call(serviceParameter, operationInfo.Method, parameterExpressions);
            var expression = Expression.Lambda<Func<TService, TRequest, CancellationToken, Task<TResponse>>>(
                invocation, false, serviceParameter, requestParameter, cancellationTokenParameter);

            var func = expression.Compile();
            return func;
        }

        protected static Func<TService, TRequest, CancellationToken, IAsyncEnumerable<TReturn>> GenerateServerStreamingMethodHandler<TRequest, TReturn>(RpcOperationInfo operationInfo)
            where TRequest : class
        {
            var requestParameter = Expression.Parameter(typeof(TRequest));
            var cancellationTokenParameter = Expression.Parameter(typeof(CancellationToken));

            List<Expression> parameterExpressions = GetParameterExpressions<TRequest>(operationInfo, requestParameter, cancellationTokenParameter);

            var serviceParameter = Expression.Parameter(typeof(TService));

            var invocation = Expression.Call(serviceParameter, operationInfo.Method, parameterExpressions);
            var expression = Expression.Lambda<Func<TService, TRequest, CancellationToken, IAsyncEnumerable<TReturn>>>(
                invocation, false, serviceParameter, requestParameter, cancellationTokenParameter);

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
            Func<TService, TRequest, CancellationToken, Task<TReturn>> serviceCaller,
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
            Func<TService, TRequest, CancellationToken, TReturn> serviceCaller,
            Func<TReturn, TResponseReturn>? responseConverter,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            TMethodBinder binder)
            where TRequest : class, IObjectRequest;

        protected abstract void AddGenericVoidAsyncMethodImpl<TRequest>(
            Func<TService, TRequest, CancellationToken, Task> serviceCaller,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            TMethodBinder binder)
            where TRequest : class, IObjectRequest;

        protected abstract void AddGenericVoidBlockingMethodImpl<TRequest>(
            Action<TService, TRequest, CancellationToken> serviceCaller,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            TMethodBinder binder)
            where TRequest : class, IObjectRequest;

        protected void AddGenericServerStreamingMethod<TRequest, TReturn, TResponseReturn>(RpcStub<TService> serviceStub, RpcOperationInfo opInfo, TMethodBinder binder)
            where TRequest : class, IObjectRequest
            where TResponseReturn : class
        {
            Func<TReturn, TResponseReturn>? responseCreator = GetResponseCreator<TReturn, TResponseReturn>(opInfo);
            RpcServerFaultHandler faultHandler = this.CreateFaultHandler(opInfo);

            var serviceCaller = GenerateServerStreamingMethodHandler<TRequest, TReturn>(opInfo);
            this.AddServerStreamingMethodImpl(serviceCaller, responseCreator, faultHandler, serviceStub, opInfo, binder);
        }

        protected abstract void AddServerStreamingMethodImpl<TRequest, TReturn, TResponseReturn>(
            Func<TService, TRequest, CancellationToken, IAsyncEnumerable<TReturn>> serviceCaller,
            Func<TReturn, TResponseReturn>? responseConverter,
            RpcServerFaultHandler faultHandler,
            RpcStub<TService> serviceStub,
            RpcOperationInfo operationInfo,
            TMethodBinder binder)
            where TRequest : class, IObjectRequest
            where TResponseReturn : class;

        protected virtual ImmutableRpcServerOptions CreateStubOptions(IRpcServerImpl server)
        {
            var registeredOptions = server.ServiceDefinitionsProvider.GetServiceOptions(typeof(TService));

            return ImmutableRpcServerOptions.Combine(this.Options, registeredOptions);
        }

        private static void CheckPropertySet(RpcPropertyInfo propertyInfo)
        {
            if (propertyInfo.PropertyTypeKind != ServiceOperationReturnKind.Standard)
            {
                throw new RpcDefinitionException($"Type {propertyInfo.Property.PropertyType} is not valid for RPC service property '{propertyInfo.Name}'.");
            }
        }

        private static Action<TService, TRequest, CancellationToken> GenerateVoidBlockingUnaryMethodHandler<TRequest>(RpcOperationInfo operationInfo)
        {
            var requestParameter = Expression.Parameter(typeof(TRequest));
            var cancellationTokenParameter = Expression.Parameter(typeof(CancellationToken));

            List<Expression> parameterExpressions = GetParameterExpressions<TRequest>(operationInfo, requestParameter, cancellationTokenParameter);

            var serviceParameter = Expression.Parameter(typeof(TService));

            var invocation = Expression.Call(serviceParameter, operationInfo.Method, parameterExpressions);
            var func = Expression.Lambda<Action<TService, TRequest, CancellationToken>>(
                invocation, false, serviceParameter, requestParameter, cancellationTokenParameter).Compile();
            return func;
        }

        private static Func<TService, TRequest, CancellationToken, Task> GenerateVoidUnaryMethodHandler<TRequest>(RpcOperationInfo operationInfo)
        {
            var requestParameter = Expression.Parameter(typeof(TRequest));
            var cancellationTokenParameter = Expression.Parameter(typeof(CancellationToken));

            List<Expression> parameterExpressions = GetParameterExpressions<TRequest>(operationInfo, requestParameter, cancellationTokenParameter);

            var serviceParameter = Expression.Parameter(typeof(TService));

            var invocation = Expression.Call(serviceParameter, operationInfo.Method, parameterExpressions);
            var func = Expression.Lambda<Func<TService, TRequest, CancellationToken, Task>>(
                invocation, false, serviceParameter, requestParameter, cancellationTokenParameter).Compile();
            return func;
        }


        private static MethodInfo GetBuilderMethod(string name, BindingFlags bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance)
        {
            return typeof(RpcServiceStubBuilder<TService, TMethodBinder>)
                .GetMethod(name, bindingFlags)
                ?? throw new NotImplementedException($"Method {name} not found on type '{typeof(RpcServiceStubBuilder<TService, TMethodBinder>)}'.");
        }

        private static List<Expression> GetParameterExpressions<TRequest>(RpcOperationInfo operationInfo, ParameterExpression requestParameter, ParameterExpression cancellationTokenParameter)
        {
            var parameterExpressions = new List<Expression>();
            var parameters = operationInfo.Method.GetParameters();

            for (int paramIndex = 0; paramIndex < parameters.Length; paramIndex++)
            {
                if (paramIndex == operationInfo.CancellationTokenIndex)
                {
                    parameterExpressions.Add(cancellationTokenParameter);
                }
                else
                {
                    int valueIndex = GetRequestValueIndex(operationInfo.RequestParameters, paramIndex);
                    if (valueIndex < 0)
                    {
                        throw new RpcDefinitionException("Failed to find RPC method parameter.");
                    }

                    parameterExpressions.Add(Expression.Field(requestParameter, typeof(TRequest), $"Value{valueIndex}"));
                }
            }

            return parameterExpressions;
        }

        private static int GetRequestValueIndex(ImmutableArray<RpcRequestParameter> requestParameters, int paramIndex)
        {
            for (int requestParamIndex = 0; requestParamIndex < requestParameters.Length; requestParamIndex++)
            {
                var rp = requestParameters[requestParamIndex];
                if (rp.Index == paramIndex)
                {
                    return requestParamIndex + 1;
                }
            }

            return -1;
        }

        private static MethodInfo GetStubMethod(string name, BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
        {
            return typeof(RpcStub).GetMethod(name, bindingFlags)
                ?? throw new NotImplementedException($"Method {name} not found on type '{nameof(RpcStub)}'.");
        }

        private static List<IRpcServerExceptionConverter> RetrieveErrorGenerators(IEnumerable<Attribute> faultAttributes)
        {
            var errorGenerators = new List<IRpcServerExceptionConverter>();
            foreach (RpcFaultAttribute faultAttribute in faultAttributes)
            {

                if (faultAttribute.FaultType != null)
                {
                    var rpcErrorGenerator = (IRpcServerExceptionConverter?)typeof(RpcFaultExceptionConverter<>)
                        .MakeGenericType(faultAttribute.FaultType)
                        .GetField(nameof(RpcFaultExceptionConverter<object>.Default), BindingFlags.Static | BindingFlags.Public)?
                        .GetValue(null) ?? throw new NotImplementedException("RpcFaultExceptionConverter Default field not found.");
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

        private void AddPlainEventHandler(RpcStub<TService> serviceStub, RpcEventInfo eventInfo, TMethodBinder binder)
        {
            var serviceParameter = Expression.Parameter(typeof(TService));
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


        private void AddUnaryMethod(RpcStub<TService> serviceStub, RpcOperationInfo opInfo, TMethodBinder binder)
        {
            if (!opInfo.ReturnType.Equals(typeof(void)))
            {
                // TODO: Cache.                
                var addUnaryMethodDelegate = (Action<RpcStub<TService>, RpcOperationInfo, TMethodBinder>)
                    GetBuilderMethod(nameof(this.AddGenericBlockingMethod))
                    .MakeGenericMethod(opInfo.RequestType, opInfo.ReturnType, opInfo.ResponseReturnType)
                    .CreateDelegate(typeof(Action<RpcStub<TService>, RpcOperationInfo, TMethodBinder>), this);

                addUnaryMethodDelegate(serviceStub, opInfo, binder);
            }
            else
            {
                // TODO: Cache.
                var addUnaryMethodDelegate = (Action<RpcStub<TService>, RpcOperationInfo, TMethodBinder>)
                    GetBuilderMethod(nameof(this.AddGenericVoidUnaryMethod))
                    .MakeGenericMethod(opInfo.RequestType)
                    .CreateDelegate(typeof(Action<RpcStub<TService>, RpcOperationInfo, TMethodBinder>), this);

                addUnaryMethodDelegate(serviceStub, opInfo, binder);

            }
        }
        private void AddServerStreamingMethod(RpcStub<TService> serviceStub, RpcOperationInfo opInfo, TMethodBinder binder)
        {
            // TODO: Cache.                
            var addUnaryMethodDelegate = (Action<RpcStub<TService>, RpcOperationInfo, TMethodBinder>)
                GetBuilderMethod(nameof(this.AddGenericServerStreamingMethod))
                .MakeGenericMethod(opInfo.RequestType, opInfo.ReturnType, opInfo.ResponseReturnType)
                .CreateDelegate(typeof(Action<RpcStub<TService>, RpcOperationInfo, TMethodBinder>), this);

            addUnaryMethodDelegate(serviceStub, opInfo, binder);
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
            var options = this.CreateStubOptions(server);
            return new RpcStub<TService>(server, options);
        }

        private FieldInfo GetBuilderField(string name, BindingFlags bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance)
        {
            return this.GetType().GetField(name, bindingFlags)
                ?? throw new NotImplementedException($"Field {name} not found on type '{this.GetType()}'.");
        }

        private Func<TReturn, TResponseReturn>? GetResponseCreator<TReturn, TResponseReturn>(RpcOperationInfo opInfo)
        {
            Func<TReturn, TResponseReturn>? responseCreator = null;
            switch (opInfo.ReturnKind)
            {
                case ServiceOperationReturnKind.Service:
                    {
                        var method = GetStubMethod(nameof(RpcStub.ConvertServiceResponse))
                            .MakeGenericMethod(typeof(TReturn));

                        var func = method.CreateDelegate(typeof(Func<TReturn, TResponseReturn>), this.serviceStub);

                        responseCreator = (Func<TReturn, TResponseReturn>)func;

                        break;
                    }

                case ServiceOperationReturnKind.ServiceArray:
                    {
                        var elementType = typeof(TReturn).GetElementType()
                            ?? throw new InvalidOperationException($"Return type '{typeof(TReturn)}' should be an array");
                        var method = GetStubMethod(nameof(RpcStub.ConvertServiceArrayResponse))
                            .MakeGenericMethod(elementType);

                        var func = method.CreateDelegate(typeof(Func<TReturn, TResponseReturn>), this.serviceStub);

                        responseCreator = (Func<TReturn, TResponseReturn>)func;

                        break;
                    }
                case ServiceOperationReturnKind.ServiceRef:
                    {
                        var method =
                            GetStubMethod(nameof(RpcStub.ConvertServiceRefResponse))
                            .MakeGenericMethod(typeof(TReturn));

                        var func = method.CreateDelegate(typeof(Func<TReturn, TResponseReturn>));

                        responseCreator = (Func<TReturn, TResponseReturn>)func;

                        break;
                    }
                case ServiceOperationReturnKind.ServiceRefArray:
                    {
                        var elementType = typeof(TReturn).GetElementType()
                            ?? throw new InvalidOperationException($"Return type '{typeof(TReturn)}' should be an array");

                        var method =
                            GetStubMethod(nameof(RpcStub.ConvertServiceRefArrayResponse))
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

        class GenericEventHandlerProducer<TEventArgs> : EventProducer<TService, TEventArgs> where TEventArgs : class
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

            private void Handler(object? s, TEventArgs e)
            {
                this.HandleEvent(e);
            }

        }

        class PlainEventHandlerProducer : EventProducer<TService, EventArgs>
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

            private void Handler(object? s, EventArgs e)
            {
                this.HandleEvent(e);
            }

        }

    }
#pragma warning restore CA1062 // Validate arguments of public methods
}
