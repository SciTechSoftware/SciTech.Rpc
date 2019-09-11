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

using SciTech.Collections;
using SciTech.Diagnostics;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Logging;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Server.Internal
{
    /// <summary>
    /// A writable stream of messages.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    public interface IRpcAsyncStreamWriter<in T>
    {
        Task WriteAsync(T message);
    }

    public interface IRpcCallContext
    {
        CancellationToken CancellationToken { get; }

        IRpcServerCallMetadata RequestHeaders { get; }
    }

#pragma warning disable CA1062 // Validate arguments of public methods
    public abstract class RpcStub
    {
        protected RpcStub(IRpcServerImpl server, ImmutableRpcServerOptions options)
        {
            this.Server = server;
            this.ServicePublisher = this.Server.ServicePublisher;

            this.AllowAutoPublish = options?.AllowAutoPublish ?? this.Server.AllowAutoPublish;
            this.Serializer = options?.Serializer ?? this.Server.Serializer;

            if (options != null && options.ExceptionConverters.Length > 0)
            {
                this.CustomFaultHandler = new RpcServerFaultHandler(this.Server.ExceptionConverters.Concat(options.ExceptionConverters));
            }
            else
            {
                this.CustomFaultHandler = this.Server.CustomFaultHandler;
            }
        }

        public bool AllowAutoPublish { get; }

        public IRpcSerializer Serializer { get; }

        public IRpcServerImpl Server { get; }

        public IRpcServicePublisher ServicePublisher { get; }

        protected RpcServerFaultHandler? CustomFaultHandler { get; }

        public static RpcObjectRef?[]? ConvertServiceRefArrayResponse<TRpcObjectRef>(TRpcObjectRef[]? typedServiceRefs) where TRpcObjectRef : RpcObjectRef
        {
            if (typedServiceRefs != null)
            {
                var serviceRefs = new List<RpcObjectRef?>();

                foreach (var serviceRef in typedServiceRefs)
                {
                    serviceRefs.Add(serviceRef?.Cast());
                }

                return serviceRefs.ToArray();
            }

            return null;
        }

        public static RpcObjectRef? ConvertServiceRefResponse<TRpcObjectRef>(TRpcObjectRef? typedServiceRef) where TRpcObjectRef : RpcObjectRef
        {
            return typedServiceRef?.Cast();
        }

        public RpcObjectRef?[]? ConvertServiceArrayResponse<TReturnService>(TReturnService[]? services) where TReturnService : class
        {
            if (services != null)
            {
                var serviceRefs = new List<RpcObjectRef?>();

                foreach (var service in services)
                {
                    serviceRefs.Add(this.ConvertServiceResponse(service));
                }

                return serviceRefs.ToArray();
            }

            return null;

        }

        public RpcObjectRef? ConvertServiceResponse<TReturnService>(TReturnService? service) where TReturnService : class
        {
            if (service != null)
            {
                RpcObjectRef? serviceRef;

                if (this.AllowAutoPublish)
                {
                    serviceRef = this.ServicePublisher.GetOrPublishInstance(service)?.Cast();
                }
                else
                {
                    serviceRef = this.ServicePublisher.GetPublishedInstance(service)?.Cast();
                }

                if (serviceRef == null)
                {
                    throw new RpcFailureException(RpcFailure.ServiceNotPublished, "Returned RPC service has not been published. To allow auto-publishing, set the AllowAutoPublish property to true.");
                }

                return serviceRef;
            }

            return null;
        }
    }

#pragma warning restore CA1062 // Validate arguments of public methods

#pragma warning disable CA1031 // Do not catch general exception types
#pragma warning disable CA1062 // Validate arguments of public methods

    public sealed class RpcStub<TService> : RpcStub where TService : class
    {
        private static readonly ILog Logger = LogProvider.GetLogger(typeof(RpcStub<>));

        private readonly IRpcServiceActivator serviceImplProvider;

        public RpcStub(IRpcServerImpl server, ImmutableRpcServerOptions options) : base(server, options)
        {
            this.serviceImplProvider = server.ServiceImplProvider;
        }

        public IServiceProvider? ServiceProvider { get; }

        public async ValueTask BeginEventProducer<TEventArgs>(
            RpcObjectRequest request,
            IServiceProvider? serviceProvider,
            EventProducer<TService, TEventArgs> eventProducer,
            IRpcCallContext context)
        {
            var service = this.GetServiceImpl(serviceProvider, request.Id);
            if (RpcStubOptions.TestDelayEventHandlers)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }

            try
            {
                await eventProducer.Run(service).ContextFree();
                Logger.Trace("EventProducer.Run returned successfully.");
            }
            catch (OperationCanceledException oce)
            {
                Logger.Trace("EventProducer.Run cancelled.", oce);
                throw;
            }
            catch (Exception e)
            {
                Logger.Trace("EventProducer.Run error.", e);
                throw;
            }
            finally
            {
            }
        }

        public async ValueTask CallServerStreamingMethod<TRequest, TResult, TResponse>(
            TRequest request,
            IServiceProvider? serviceProvider,
            IRpcCallContext context,
            IRpcAsyncStreamWriter<TResponse> responseWriter,
            Func<TService, TRequest, CancellationToken, IAsyncEnumerable<TResult>> implCaller,
            Func<TResult, TResponse>? responseConverter,
            RpcServerFaultHandler? faultHandler,
            IRpcSerializer serializer) where TRequest : IObjectRequest
        {
            try
            {
                var (service, interceptDisposables) = await this.BeginCall(serviceProvider, request.Id, context).ContextFree();

                try
                {
                    // Call the actual implementation method.
                    var result = implCaller(service, request, context.CancellationToken);
                    await foreach( var ret in result.ConfigureAwait(false))
                    {
                        context.CancellationToken.ThrowIfCancellationRequested();
                        var response = CreateResponse(responseConverter, ret);

                        if (response.Error == null)
                        {
                            await responseWriter.WriteAsync(response.Result).ContextFree();
                        } else
                        {
                            // TODO: Implement
                            throw new RpcFailureException(RpcFailure.Unknown);
                        }
                    }
                }
                catch (Exception e)
                {
                    if (this.HandleRpcError(e, faultHandler, serializer) is RpcError rpcError)
                    {
                        // TODO: Implement (should write RpcResponse<> if allowed).
                        throw new RpcFailureException(RpcFailure.Unknown);
                        //return new RpcResponse<TResponse>(rpcError);
                    }

                    throw;
                }
                finally
                {
                    this.EndCall(interceptDisposables);
                }
            }
            catch (Exception e)
            {
                if (CreateRpcErrorResponse<TResponse>(e) is RpcResponse<TResponse> errorResponse)
                {
                    // TODO: Implement (should maybe write RpcResponse<> if allowed?).
                    throw new RpcFailureException(RpcFailure.Unknown);
                    //return new RpcResponse<TResponse>(rpcError);
                }

                throw;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TRequest"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <typeparam name="TResponse">Indicates whether the returned response should be typed or not. 
        /// Must be <see cref="RpcResponse{T}"/> or <see cref="object"/>.</typeparam>
        /// <param name="request"></param>
        /// <param name="context"></param>
        /// <param name="implCaller"></param>
        /// <returns></returns>
        public async ValueTask<RpcResponse<TResponse>> CallAsyncMethod<TRequest, TResult, TResponse>(
            TRequest request,
            IServiceProvider? serviceProvider,
            IRpcCallContext context,
            Func<TService, TRequest, CancellationToken, Task<TResult>> implCaller,
            Func<TResult, TResponse>? responseConverter,
            RpcServerFaultHandler? faultHandler,
            IRpcSerializer serializer) where TRequest : IObjectRequest
        {
            try
            {
                var (service, interceptDisposables) = await this.BeginCall(serviceProvider, request.Id, context);

                try
                {
                    // Call the actual implementation method.
                    var result = await implCaller(service, request, context.CancellationToken).ContextFree();
                    context.CancellationToken.ThrowIfCancellationRequested();

                    return CreateResponse(responseConverter, result);
                }
                catch (Exception e)
                {
                    if (this.HandleRpcError(e, faultHandler, serializer) is RpcError rpcError)
                    {
                        return new RpcResponse<TResponse>(rpcError);
                    }

                    throw;
                }
                finally
                {
                    this.EndCall(interceptDisposables);
                }
            }
            catch (Exception e)
            {
                if (CreateRpcErrorResponse<TResponse>(e) is RpcResponse<TResponse> errorResponse)
                {
                    return errorResponse;
                }

                throw;
            }
        }

        public async ValueTask<RpcResponse<TResponse>> CallBlockingMethod<TRequest, TResult, TResponse>(
            TRequest request,
            IServiceProvider? serviceProvider,
            IRpcCallContext context,
            Func<TService, TRequest, CancellationToken, TResult> implCaller,
            Func<TResult, TResponse>? responseConverter,
            RpcServerFaultHandler? faultHandler,
            IRpcSerializer serializer) where TRequest : IObjectRequest
        {
            try
            {
                var (service, interceptDisposables) = await this.BeginCall(serviceProvider, request.Id, context).ContextFree();

                try
                {
                    // Call the actual implementation method.
                    var result = implCaller(service, request, context.CancellationToken);
                    context.CancellationToken.ThrowIfCancellationRequested();

                    return CreateResponse(responseConverter, result);
                }
                catch (Exception e)
                {
                    if (this.HandleRpcError(e, faultHandler, serializer) is RpcError rpcError)
                    {
                        return new RpcResponse<TResponse>(rpcError);
                    }

                    throw;
                }
                finally
                {
                    this.EndCall(interceptDisposables);
                }
            }
            catch (Exception e)
            {
                if (CreateRpcErrorResponse<TResponse>(e) is RpcResponse<TResponse> errorResponse)
                {
                    return errorResponse;
                }

                throw;
            }
        }

        public async ValueTask<RpcResponse> CallVoidAsyncMethod<TRequest>(
            TRequest request,
            IServiceProvider? serviceProvider,
            IRpcCallContext context,
            Func<TService, TRequest, CancellationToken, Task> implCaller,
            RpcServerFaultHandler? faultHandler,
            IRpcSerializer serializer) where TRequest : IObjectRequest
        {
            try
            {
                var (service, interceptDisposables) = await this.BeginCall(serviceProvider, request.Id, context);

                try
                {
                    // Call the actual implementation method.
                    await implCaller(service, request, context.CancellationToken).ContextFree();
                    context.CancellationToken.ThrowIfCancellationRequested();

                    return new RpcResponse();
                }
                catch (Exception e)
                {
                    if (this.HandleRpcError(e, faultHandler, serializer) is RpcError rpcError)
                    {
                        return new RpcResponse(rpcError);
                    }

                    throw;
                }
                finally
                {
                    this.EndCall(interceptDisposables);
                }
            }
            catch (Exception e)
            {
                if (CreateRpcErrorResponse(e) is RpcResponse errorResponse)
                {
                    return errorResponse;
                }

                throw;
            }
        }

        public async ValueTask<RpcResponse> CallVoidBlockingMethod<TRequest>(
            TRequest request,
            IServiceProvider? serviceProvider,
            IRpcCallContext context,
            Action<TService, TRequest, CancellationToken> implCaller,
            RpcServerFaultHandler? faultHandler,
            IRpcSerializer serializer)
            where TRequest : IObjectRequest
        {
            try
            {
                var (service, interceptDisposables) = await this.BeginCall(serviceProvider, request.Id, context);

                try
                {
                    // Call the actual implementation method.
                    implCaller(service, request, context.CancellationToken);
                    context.CancellationToken.ThrowIfCancellationRequested();

                    return new RpcResponse();
                }
                catch (Exception e)
                {
                    if (this.HandleRpcError(e, faultHandler, serializer) is RpcError rpcError)
                    {
                        return new RpcResponse(rpcError);
                    }

                    throw;
                }
                finally
                {
                    this.EndCall(interceptDisposables);
                }
            }
            catch (Exception e)
            {
                if (CreateRpcErrorResponse(e) is RpcResponse errorResponse)
                {
                    return errorResponse;
                }

                throw;
            }
        }

        private static RpcResponse<TResponse> CreateResponse<TResult, TResponse>(Func<TResult, TResponse>? responseConverter, TResult result)
        {
            RpcResponse<TResponse> rpcResponse;
            if (responseConverter == null)
            {
                if (typeof(TResponse) == typeof(TResult))
                {
                    if (result is TResponse response)
                    {
                        rpcResponse = new RpcResponse<TResponse>(response);
                    }
                    else
                    {
                        // This should only happen if result is null
                        rpcResponse = new RpcResponse<TResponse>(default(TResponse)!);
                    }
                }
                else
                {
                    throw new RpcFailureException(RpcFailure.RemoteDefinitionError, "Response converter is required if response is not the same as result.");
                }
            }
            else
            {
                rpcResponse = new RpcResponse<TResponse>(responseConverter(result));
            }

            return rpcResponse;
        }

        private static RpcError? CreateRpcError(Exception e)
        {
            if (e is RpcServiceUnavailableException)
            {
                return new RpcError
                {
                    ErrorType = WellKnownRpcErrors.ServiceUnavailable,
                    Message = e.Message
                };
            }

            return null;

            //return new RpcError
            //{
            //    ErrorType = WellKnownRpcErrors.Failure,
            //    Message = "Failed to process RPC call"
            //};
        }

        private static RpcResponse<TResponse>? CreateRpcErrorResponse<TResponse>(Exception e)
        {
            if (CreateRpcError(e) is RpcError rpcError)
            {
                return new RpcResponse<TResponse>(rpcError);
            }

            return null;
        }

        private static RpcResponse? CreateRpcErrorResponse(Exception e)
        {
            if (CreateRpcError(e) is RpcError rpcError)
            {
                return new RpcResponse(rpcError);
            }

            return null;
        }

        private static RpcError? TryConvertToFault(Exception e, IReadOnlyList<IRpcServerExceptionConverter> converters, RpcServerFaultHandler faultHandler, IRpcSerializer serializer)
        {
            RpcError? rpcError = null;
            foreach (var converter in converters)
            {
                // We have at least one declared exception handlers
                var convertedFault = converter.CreateFault(e);

                if (convertedFault != null && faultHandler.IsFaultDeclared(convertedFault.FaultCode))
                {
                    byte[]? detailsData = null;
                    if (convertedFault.Details != null)
                    {
                        detailsData = serializer.ToBytes(convertedFault.Details);
                    }

                    rpcError = new RpcError { ErrorType = WellKnownRpcErrors.Fault, FaultCode = converter.FaultCode, Message = convertedFault.Message, FaultDetails = detailsData };
                    break;
                }
            }

            return rpcError;
        }

        /// <summary>
        /// Prepares a call to the RPC implementation method. Must be combined with a corresponding call to 
        /// <see cref="EndCall(CompactList{IDisposable?})"/>
        /// <para>IMPORTANT! This method must not be an async method, since it likely that an interceptor will update
        /// an AsyncLocal (e.g. session or security token). Changes to AsyncLocals will not propagate out of an async 
        /// method.</para>
        /// </summary>
        /// <param name="serviceProvider"></param>
        /// <param name="objectId"></param>
        /// <param name="context"></param>
        /// <returns>A tuple containing the service implementation instance, and an array of disposables that must 
        /// be disposed when the call is finished.</returns>
        private ValueTask<ValueTuple<TService, CompactList<IDisposable?>>> BeginCall(IServiceProvider? serviceProvider, RpcObjectId objectId, IRpcCallContext context)
        {
            async ValueTask<ValueTuple<TService, CompactList<IDisposable?>>> AwaitPendingInterceptors(
                TService service,
                CompactList<Task<IDisposable>> pendingInterceptors,
                CompactList<IDisposable?> interceptDisposables)
            {
                var disposables = await Task.WhenAll(pendingInterceptors).ContextFree();
                interceptDisposables.AddRange(disposables);
                return (service, interceptDisposables);
            }

            var service = this.GetServiceImpl(serviceProvider, objectId);
            var interceptors = this.Server.CallInterceptors;

            CompactList<IDisposable?> interceptDisposables;
            CompactList<Task<IDisposable>> pendingInterceptors;
            IRpcServerCallMetadata? rpcMetaData = null;

            if (interceptors.Length > 0)
            {
                rpcMetaData = context.RequestHeaders;
                interceptDisposables.Reset(interceptors.Length);
            }

            try
            {
                for (int index = 0; index < interceptors.Length; index++)
                {
                    var interceptor = interceptors[index];
                    var interceptTask = interceptor(rpcMetaData!);
                    if (interceptTask.Status != TaskStatus.RanToCompletion)
                    {
                        // TODO: This is completely untested. And investigate how an async interceptor
                        // will be able to update an AsyncLocal and propagate the change out from this method.
                        pendingInterceptors.Add(interceptTask);
                    }
                    else
                    {
                        interceptDisposables[index] = interceptTask.AwaiterResult();
                    }
                }

                if (pendingInterceptors.IsEmpty)
                {
                    return new ValueTask<(TService, CompactList<IDisposable?>)>((service, interceptDisposables));
                }

                return AwaitPendingInterceptors(service, pendingInterceptors, interceptDisposables);
            }
            catch (Exception)
            {
                // An interceptor threw an exception. Try to cleanup and then rethrow.
                // TODO: Log.
                for (int index = interceptors.Length - 1; index >= 0; index--)
                {
                    try { interceptDisposables![index]?.Dispose(); } catch { }
                }

                throw;
            }
        }

        private void EndCall(CompactList<IDisposable?> interceptDisposables)
        {
            for (int index = interceptDisposables.Count - 1; index >= 0; index--)
            {
                try { interceptDisposables[index]?.Dispose(); } catch { /* TODO: Log? */ }
            }
        }

        private TService GetServiceImpl(IServiceProvider? serviceProvider, RpcObjectId objectId)
        {
            if (this.serviceImplProvider.GetServiceImpl<TService>(serviceProvider, objectId) is TService service)
            {
                return service;
            }

            throw new RpcServiceUnavailableException($"Service object '{objectId}' ({typeof(TService).Name}) not available.");
        }

        private RpcError? HandleRpcError(Exception e, RpcServerFaultHandler? declaredFaultHandler, IRpcSerializer serializer)
        {
            // TODO: Rethrow if operation is tagged as "RpcNoFault"

            RpcError? rpcError = null;
            if (declaredFaultHandler != null)
            {
                // Start by checking custom exception converters.
                if (this.CustomFaultHandler is RpcServerFaultHandler customFaultHandler)
                {
                    if (customFaultHandler.TryGetExceptionConverter(e, out var customConverters))
                    {
                        // Using the methodStub fault handler as the faultHandler argument, since 
                        // this faultHandler is used to check whether the fault is declared for the specific operation.
                        rpcError = TryConvertToFault(e, customConverters!, declaredFaultHandler, serializer);
                    }
                }

                if (rpcError == null)
                {
                    // Not handled by a custom converter, so let's try the declared converters. 
                    if (declaredFaultHandler.TryGetExceptionConverter(e, out var declaredConverters))
                    {
                        rpcError = TryConvertToFault(e, declaredConverters!, declaredFaultHandler, serializer);
                    }
                }
            }


            if (rpcError == null)
            {
                // Exception not handled by any custom or declared fault handler. Let's
                // perform default handling.
                if (e is RpcFaultException faultException)
                {
                    rpcError = new RpcError { ErrorType = WellKnownRpcErrors.Fault, FaultCode = faultException.FaultCode, Message = faultException.Message };
                }
                else if (e is RpcFailureException)
                {
                    rpcError = new RpcError { ErrorType = WellKnownRpcErrors.Failure, Message = e.Message };
                }
                else
                {
                    // TODO: Log and/or call unhandled exception handler. This is an unexepected error that may be serious and 
                    // should cause the server to shutdown.
                    // Note, in case the server is shutdown, it would be very good if the response is sent to the client first (add 
                    // suitable tests).

                    //// TODO: Implement IncludeExceptionDetailInFaults
                    //string message = "The server was unable to process the request due to an internal error. "
                    //    + "For more information about the error, turn on IncludeExceptionDetailInFaults to send the exception information back to the client.";
                    //rpcError = new RpcError { ErrorType = WellKnownRpcErrors.Fault, FaultCode = "", Message = message };
                }
            }

            return rpcError;
        }
    }

#pragma warning restore CA1062 // Validate arguments of public methods
#pragma warning restore CA1031 // Do not catch general exception types

    /// <summary>
    /// Contains global stub options, mainly intended for testing.
    /// </summary>
    internal static class RpcStubOptions
    {
        /// <summary>
        /// Indicates that a delay should be added to event handler Add/Remove for testing purposes.
        /// Should only be set to true when running tests.
        /// </summary>
        internal static bool TestDelayEventHandlers = false;
    }
}
