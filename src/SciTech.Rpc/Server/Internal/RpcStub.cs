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
using SciTech.Rpc.Internal;
using SciTech.Rpc.Logging;
using SciTech.Rpc.Serialization;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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


    /// <summary>
    /// Cancellation support is currently only used internally.
    /// </summary>
    public interface IRpcCallContextWithCancellation : IRpcCallContext
    {

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
            EventProducer<TService, TEventArgs> eventProducer)
        {
            var service = this.GetServiceImpl(serviceProvider, request.Id);
            if (RpcStubOptions.TestDelayEventHandlers)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }

            try
            {
                await eventProducer.Run(service.Service!).ContextFree();
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

        public async ValueTask<RpcResponse<TResponse>> CallAsyncMethod<TRequest, TResult, TResponse>(
            TRequest request,
            IServiceProvider? serviceProvider,
            IRpcCallContextWithCancellation context,
            Func<TService, TRequest, CancellationToken, Task<TResult>> implCaller,
            Func<TResult, TResponse>? responseConverter)
            where TRequest : IObjectRequest
        {
            var (activatedService, interceptDisposables) = await this.BeginCall(serviceProvider, request.Id, context);

            try
            {
                // Call the actual implementation method.
                var result = await implCaller(activatedService.Service, request, context.CancellationToken).ContextFree();
                context.CancellationToken.ThrowIfCancellationRequested();

                return CreateResponse(responseConverter, result);
            }
            finally
            {
                EndCall(activatedService, interceptDisposables);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TRequest"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <typeparam name="TResponse">Indicates whether the returned response should be typed or not. 
        /// Must be <see cref="RpcResponseWithError{T}"/> or <see cref="object"/>.</typeparam>
        /// <param name="request"></param>
        /// <param name="context"></param>
        /// <param name="implCaller"></param>
        /// <returns></returns>
        public async ValueTask<RpcResponseWithError<TResponse>> CallAsyncMethodWithError<TRequest, TResult, TResponse>(
            TRequest request,
            IServiceProvider? serviceProvider,
            IRpcCallContextWithCancellation context,
            Func<TService, TRequest, CancellationToken, Task<TResult>> implCaller,
            Func<TResult, TResponse>? responseConverter,
            RpcServerFaultHandler? faultHandler,
            IRpcSerializer serializer) where TRequest : IObjectRequest
        {
            try
            {
                var (activatedService, interceptDisposables) = await this.BeginCall(serviceProvider, request.Id, context);

                try
                {
                    // Call the actual implementation method.
                    var result = await implCaller(activatedService.Service, request, context.CancellationToken).ContextFree();

                    context.CancellationToken.ThrowIfCancellationRequested();

                    return CreateResponseWithError(responseConverter, result);
                }
                catch (Exception e)
                {
                    this.HandleRpcError(e, faultHandler);
                    throw;
                }
                finally
                {
                    EndCall(activatedService, interceptDisposables);
                }
            }
            catch (Exception e)
            {
                this.Server.HandleCallException(e, serializer);
                //if (CreateRpcErrorResponse<TResponse>(e) is RpcResponseWithError<TResponse> errorResponse)
                //{
                //    return errorResponse;
                //}

                throw;
            }
        }

        public ValueTask<RpcResponse<TResponse>> CallBlockingMethod<TRequest, TResult, TResponse>(
            TRequest request,
            IRpcCallContextWithCancellation context,
            Func<TService, TRequest, CancellationToken, TResult> implCaller,
            Func<TResult, TResponse>? responseConverter,
            IServiceProvider? serviceProvider) where TRequest : IObjectRequest
        {
            var beginCallTask = this.BeginCall(serviceProvider, request.Id, context);
            if (beginCallTask.IsCompletedSuccessfully)
            {
                var (activatedService, interceptDisposables) = beginCallTask.Result;
                return new ValueTask<RpcResponse<TResponse>>(
                    this.DoCall(request, context, implCaller, responseConverter, activatedService, interceptDisposables));
            }
            else
            {
                async ValueTask<RpcResponse<TResponse>> AwaitAnDoCall()
                {
                    var (activatedService, interceptDisposables) = await beginCallTask.ContextFree();
                    return this.DoCall(request, context, implCaller, responseConverter, activatedService, interceptDisposables);
                }

                return AwaitAnDoCall();
            }

        }

        public ValueTask<RpcResponseWithError<TResponse>> CallBlockingMethodWithError<TRequest, TResult, TResponse>(
            TRequest request,
            IRpcCallContextWithCancellation context,
            Func<TService, TRequest, CancellationToken, TResult> implCaller,
            Func<TResult, TResponse>? responseConverter,
            RpcServerFaultHandler? faultHandler,
            IRpcSerializer serializer,
            IServiceProvider? serviceProvider) where TRequest : IObjectRequest
        {
            try
            {
                var beginCallTask = this.BeginCall(serviceProvider, request.Id, context);
                if (beginCallTask.IsCompletedSuccessfully)
                {
                    var (activatedService, interceptDisposables) = beginCallTask.Result;

                    return new ValueTask<RpcResponseWithError<TResponse>>(
                        this.DoCallWithError(request, context, implCaller, responseConverter, faultHandler, serializer, activatedService, interceptDisposables));
                }
                else
                {
                    async ValueTask<RpcResponseWithError<TResponse>> AwaitAnDoCall()
                    {
                        try
                        {
                            var (activatedService, interceptDisposables) = await beginCallTask.ContextFree();
                            return this.DoCallWithError(request, context, implCaller, responseConverter, faultHandler, serializer, activatedService, interceptDisposables);
                        }
                        catch (Exception e)
                        {
                            //if (CreateRpcErrorResponse<TResponse>(e) is RpcResponseWithError<TResponse> errorResponse)
                            //{
                            //    return errorResponse;
                            //}

                            throw;
                        }
                    }

                    return AwaitAnDoCall();
                }

            }
            catch (Exception e)
            {
                this.Server.HandleCallException(e, serializer);

                //if (CreateRpcErrorResponse<TResponse>(e) is RpcResponseWithError<TResponse> errorResponse)
                //{
                //    return new ValueTask<RpcResponseWithError<TResponse>>(errorResponse);
                //}

                throw;
            }
        }

        public async ValueTask CallServerStreamingMethod<TRequest, TResult, TResponse>(
            TRequest request,
            IServiceProvider? serviceProvider,
            IRpcCallContextWithCancellation context,
            IRpcAsyncStreamWriter<TResponse> responseWriter,
            Func<TService, TRequest, CancellationToken, IAsyncEnumerable<TResult>> implCaller,
            Func<TResult, TResponse>? responseConverter,
            RpcServerFaultHandler? faultHandler,
            IRpcSerializer serializer) where TRequest : IObjectRequest
        {
            try
            {
                var (activatedService, interceptDisposables) = await this.BeginCall(serviceProvider, request.Id, context).ContextFree();

                try
                {
#pragma warning disable CA2000 // Dispose objects before losing scope
                    var enumCts = new CancellationTokenSource();
                    var combinedCts = context.CancellationToken.CanBeCanceled
                        ? CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, enumCts.Token)
                        : enumCts;
#pragma warning restore CA2000 // Dispose objects before losing scope
                    IAsyncEnumerator<TResult>? asyncEnum = null;
                    Task<bool>? taskHasNext = null;

                    try
                    { 
                        // Call the actual implementation method.
                        var result = implCaller(activatedService.Service, request, combinedCts.Token);
                        if (!activatedService.ShouldDispose || !(activatedService.Service is IDisposable))
                        {
                            // Avoid keeping a reference to the activated service unless it needs to be disposed.
                            // This will allow auto-published services to be GCed while running a long-running
                            // streaming call.
                            activatedService = default;
                        }

                        // Async enumerator implementation is a bit complicated, since we want to end  the 
                        // loop if the service is unpublished or if an auto-published instance is GCed.
                        // Cannot use await foreach because we need a timeout for each MoveNextAsync and 
                        // not just for the whole numerator. Furthermore the timeout should not actually end
                        // the enumeration, it should just wake it up so that we can check if the service
                        // is still published.
                        asyncEnum = result.GetAsyncEnumerator(combinedCts.Token);

                        while (true)
                        {
                            if (taskHasNext == null)
                            {
                                taskHasNext = asyncEnum.MoveNextAsync().AsTask();
                            }

                            using (var moveNextCts = new CancellationTokenSource())
                            {
                                var taskTimeOut = Task.Delay(RpcStubOptions.StreamingResponseWaitTime, moveNextCts.Token);
                                var finishedTask = await Task.WhenAny(taskHasNext, taskTimeOut).ContextFree();

                                context.CancellationToken.ThrowIfCancellationRequested();
                                this.CheckPublished(request.Id);

                                if (finishedTask == taskHasNext)
                                {
                                    // We don't wan't any lingering delay timers.
                                    moveNextCts.Cancel();

                                    bool hasNext = await taskHasNext.ContextFree();
                                    taskHasNext = null;

                                    if (!hasNext)
                                    {
                                        break;
                                    }


                                    var response = CreateResponseWithError(responseConverter, asyncEnum.Current);

                                    if (response.Error == null)
                                    {
                                        await responseWriter.WriteAsync(response.Result).ContextFree();
                                    }
                                    else
                                    {
                                        // TODO: Implement
                                        throw new RpcFailureException(RpcFailure.Unknown);
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        async Task CleanupAsyncEnum()
                        {
                            if (asyncEnum != null)
                            {
                                try { await asyncEnum.DisposeAsync().ContextFree(); } catch { }
                            }

                            if (combinedCts != enumCts)
                            {
                                combinedCts.Dispose();
                            }
                            enumCts.Dispose();
                        }

                        if ( taskHasNext != null )
                        {
                            // Still waiting for next enumerator item so the enumerator cannot be disposed yet.
                            // Let's cancel the enumerator and dispose once finished. Let's not 
                            // wait for cancellation to finish, since it may take awhile for the enumerator to notice it.
                            enumCts.Cancel();

                            taskHasNext.ContinueWith(t =>
                               {
                                   CleanupAsyncEnum().Forget();
                               }, TaskScheduler.Default).Forget();
                        } else
                        {
                            await CleanupAsyncEnum().ContextFree();
                        }
                    }
                }
                catch (Exception e)
                {
                    // TODO: Implement (should write RpcResponse<> if allowed). ???
                    this.HandleRpcError(e, faultHandler);
                    throw;
                }
                finally
                {
                    EndCall(activatedService, interceptDisposables);
                }
            }
            catch (Exception e)
            {
                //if (CreateRpcErrorResponse<TResponse>(e) is RpcResponseWithError<TResponse> errorResponse)
                //{
                //    // TODO: Implement (should maybe write RpcResponse<> if allowed?).
                //    throw new RpcFailureException(RpcFailure.Unknown);
                //    //return new RpcResponse<TResponse>(rpcError);
                //}

                throw;
            }
        }

        public async ValueTask<RpcResponse> CallVoidAsyncMethod<TRequest>(
            TRequest request,
            IServiceProvider? serviceProvider,
            IRpcCallContextWithCancellation context,
            Func<TService, TRequest, CancellationToken, Task> implCaller) where TRequest : IObjectRequest
        {
            var (activatedService, interceptDisposables) = await this.BeginCall(serviceProvider, request.Id, context);

            try
            {
                // Call the actual implementation method.
                await implCaller(activatedService.Service, request, context.CancellationToken).ContextFree();
                context.CancellationToken.ThrowIfCancellationRequested();

                return new RpcResponse();
            }
            finally
            {
                EndCall(activatedService, interceptDisposables);
            }
        }


        public async ValueTask<RpcResponseWithError> CallVoidAsyncMethodWithError<TRequest>(
            TRequest request,
            IServiceProvider? serviceProvider,
            IRpcCallContextWithCancellation context,
            Func<TService, TRequest, CancellationToken, Task> implCaller,
            RpcServerFaultHandler? faultHandler,
            IRpcSerializer serializer) where TRequest : IObjectRequest
        {
            try
            {
                var (activatedService, interceptDisposables) = await this.BeginCall(serviceProvider, request.Id, context);

                try
                {
                    // Call the actual implementation method.
                    await implCaller(activatedService.Service, request, context.CancellationToken).ContextFree();
                    context.CancellationToken.ThrowIfCancellationRequested();

                    return new RpcResponseWithError();
                }
                catch (Exception e)
                {
                    this.HandleRpcError(e, faultHandler);
                    throw;
                }
                finally
                {
                    EndCall(activatedService, interceptDisposables);
                }
            }
            catch (Exception e)
            {
                this.Server.HandleCallException(e, serializer);
                throw;
            }
        }

        public async ValueTask<RpcResponse> CallVoidBlockingMethod<TRequest>(
            TRequest request,
            IServiceProvider? serviceProvider,
            IRpcCallContextWithCancellation context,
            Action<TService, TRequest, CancellationToken> implCaller)
            where TRequest : IObjectRequest
        {
            var (activatedService, interceptDisposables) = await this.BeginCall(serviceProvider, request.Id, context);

            try
            {
                // Call the actual implementation method.
                implCaller(activatedService.Service, request, context.CancellationToken);
                context.CancellationToken.ThrowIfCancellationRequested();

                return new RpcResponse();
            }
            finally
            {
                EndCall(activatedService, interceptDisposables);
            }
        }

        public async ValueTask<RpcResponseWithError> CallVoidBlockingMethodWithError<TRequest>(
            TRequest request,
            IServiceProvider? serviceProvider,
            IRpcCallContextWithCancellation context,
            Action<TService, TRequest, CancellationToken> implCaller,
            RpcServerFaultHandler? faultHandler,
            IRpcSerializer serializer)
            where TRequest : IObjectRequest
        {
            try
            {
                var (activatedService, interceptDisposables) = await this.BeginCall(serviceProvider, request.Id, context);

                try
                {
                    // Call the actual implementation method.
                    implCaller(activatedService.Service, request, context.CancellationToken);
                    context.CancellationToken.ThrowIfCancellationRequested();

                    return new RpcResponseWithError();
                }
                catch (Exception e)
                {
                    this.HandleRpcError(e, faultHandler);
                    throw;
                }
                finally
                {
                    EndCall(activatedService, interceptDisposables);
                }
            }
            catch (Exception e)
            {
                this.Server.HandleCallException(e, serializer);
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
                    throw new RpcFailureException(RpcFailure.RemoteDefinitionError, "Response converter is required if response type is not the same as result type.");
                }
            }
            else
            {
                rpcResponse = new RpcResponse<TResponse>(responseConverter(result));
            }

            return rpcResponse;
        }


        private static RpcResponseWithError<TResponse> CreateResponseWithError<TResult, TResponse>(Func<TResult, TResponse>? responseConverter, TResult result)
        {
            RpcResponseWithError<TResponse> rpcResponse;
            if (responseConverter == null)
            {
                if (typeof(TResponse) == typeof(TResult))
                {
                    if (result is TResponse response)
                    {
                        rpcResponse = new RpcResponseWithError<TResponse>(response);
                    }
                    else
                    {
                        // This should only happen if result is null
                        rpcResponse = new RpcResponseWithError<TResponse>(default(TResponse)!);
                    }
                }
                else
                {
                    throw new RpcFailureException(RpcFailure.RemoteDefinitionError, "Response converter is required if response type is not the same as result type.");
                }
            }
            else
            {
                rpcResponse = new RpcResponseWithError<TResponse>(responseConverter(result));
            }

            return rpcResponse;
        }

        private static RpcError? CreateRpcError(Exception e) => RpcError.TryCreate(e, null);

        //private static RpcResponseWithError<TResponse>? CreateRpcErrorResponse<TResponse>(Exception e)
        //{
        //    if (CreateRpcError(e) is RpcError rpcError)
        //    {
        //        return new RpcResponseWithError<TResponse>(rpcError);
        //    }

        //    return null;
        //}

        //private static RpcResponseWithError? CreateRpcErrorResponse(Exception e)
        //{
        //    if (CreateRpcError(e) is RpcError rpcError)
        //    {
        //        return new RpcResponseWithError(rpcError);
        //    }

        //    return null;
        //}

        private static void EndCall(in ActivatedService<TService> activatedService, CompactList<IDisposable?> interceptDisposables)
        {
            if (activatedService.ShouldDispose)
            {
                (activatedService.Service as IDisposable)?.Dispose();
            }

            for (int index = interceptDisposables.Count - 1; index >= 0; index--)
            {
                try { interceptDisposables[index]?.Dispose(); } catch { /* TODO: Log? */ }
            }
        }

        private static void TryConvertToFault(
            Exception e, IReadOnlyList<IRpcServerExceptionConverter> converters, 
            RpcServerFaultHandler faultHandler)
        {
            foreach (var converter in converters)
            {
                // We have at least one declared exception handlers
                var convertedFault = converter.CreateFault(e);

                if (convertedFault != null && faultHandler.IsFaultDeclared(convertedFault.FaultCode))
                {
                    throw convertedFault;
                    //byte[]? detailsData = null;
                    //if (convertedFault.Details != null)
                    //{
                    //    detailsData = serializer.Serialize(convertedFault.Details, convertedFault.Details.GetType());
                    //}

                    //throw new RpcFaultException(converter.FaultCode, convertedFault.Message, convertedFault.Details);
                    ////rpcError = new RpcError { ErrorType = WellKnownRpcErrors.Fault, FaultCode = converter.FaultCode, Message = convertedFault.Message, FaultDetails = detailsData };
                    //// break;
                }
            }

            //return rpcError;
        }

        private void CheckPublished(RpcObjectId objectId)
        {
            if (!this.serviceImplProvider.CanGetActivatedService<TService>(objectId))
            {
                throw CreateServiceUnavailableException(objectId);
            }
        }

        /// <summary>
        /// <para>
        /// Prepares a call to the RPC implementation method. Must be combined with a corresponding call to 
        /// <see cref="EndCall(in ActivatedService{TService}, CompactList{IDisposable?})"/>.
        /// </para>
        /// <para>IMPORTANT! This method must not be an async method, since it's likely that an interceptor will update
        /// an AsyncLocal (e.g. session or security token). Changes to AsyncLocals will not propagate out of an async 
        /// method.</para>
        /// </summary>
        /// <param name="serviceProvider"></param>
        /// <param name="objectId"></param>
        /// <param name="context"></param>
        /// <returns>A tuple containing the service implementation instance, and an array of disposables that must 
        /// be disposed when the call is finished.</returns>
        private ValueTask<ValueTuple<ActivatedService<TService>, CompactList<IDisposable?>>> BeginCall(IServiceProvider? serviceProvider, RpcObjectId objectId, IRpcCallContextWithCancellation context)
        {
            async ValueTask<ValueTuple<ActivatedService<TService>, CompactList<IDisposable?>>> AwaitPendingInterceptors(
                ActivatedService<TService> service,
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

            if (interceptors.Length > 0)
            {
                interceptDisposables.Reset(interceptors.Length);
            }

            try
            {
                for (int index = 0; index < interceptors.Length; index++)
                {
                    var interceptor = interceptors[index];
                    var interceptTask = interceptor(context);
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
                    return new ValueTask<(ActivatedService<TService>, CompactList<IDisposable?>)>((service, interceptDisposables));
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

        private RpcResponse<TResponse> DoCall<TRequest, TResult, TResponse>(
            TRequest request,
            IRpcCallContextWithCancellation context,
            Func<TService, TRequest, CancellationToken, TResult> implCaller,
            Func<TResult, TResponse>? responseConverter,
            in ActivatedService<TService> activatedService,
            CompactList<IDisposable?> interceptDisposables) where TRequest : IObjectRequest
        {
            try
            {
                // Call the actual implementation method.
                var result = implCaller(activatedService.Service, request, context.CancellationToken);
                context.CancellationToken.ThrowIfCancellationRequested();

                return CreateResponse(responseConverter, result);
            }
            finally
            {
                EndCall(activatedService, interceptDisposables);
            }
        }

        private RpcResponseWithError<TResponse> DoCallWithError<TRequest, TResult, TResponse>(
            TRequest request,
            IRpcCallContextWithCancellation context,
            Func<TService, TRequest, CancellationToken, TResult> implCaller,
            Func<TResult, TResponse>? responseConverter,
            RpcServerFaultHandler? faultHandler,
            IRpcSerializer serializer,
            in ActivatedService<TService> activatedService,
            CompactList<IDisposable?> interceptDisposables) where TRequest : IObjectRequest
        {
            try
            {
                // Call the actual implementation method.
                var result = implCaller(activatedService.Service, request, context.CancellationToken);
                context.CancellationToken.ThrowIfCancellationRequested();

                return CreateResponseWithError(responseConverter, result);
            }
            catch (Exception e)
            {
                this.HandleRpcError(e, faultHandler);
                throw;
            }
            finally
            {
                EndCall(activatedService, interceptDisposables);
            }
        }

        private ActivatedService<TService> GetServiceImpl(IServiceProvider? serviceProvider, RpcObjectId objectId)
        {
            var activatedService = this.serviceImplProvider.GetActivatedService<TService>(serviceProvider, objectId);
            if (activatedService != null)
            {
                return activatedService.Value;
            }

            throw CreateServiceUnavailableException(objectId);
        }

        private static RpcServiceUnavailableException CreateServiceUnavailableException(RpcObjectId objectId)
        {
            return new RpcServiceUnavailableException(objectId != RpcObjectId.Empty
                ? $"Service object '{objectId}' ({typeof(TService).Name}) not available."
                : $"Singleton service '{typeof(TService).Name}' not available.");
        }

        private void HandleRpcError(Exception e, RpcServerFaultHandler? declaredFaultHandler)
        {
            if (declaredFaultHandler != null)
            {
                // Start by checking custom exception converters.
                if (this.CustomFaultHandler is RpcServerFaultHandler customFaultHandler)
                {
                    if (customFaultHandler.TryGetExceptionConverter(e, out var customConverters))
                    {
                        // Using the methodStub fault handler as the faultHandler argument, since 
                        // this faultHandler is used to check whether the fault is declared for the specific operation.
                        TryConvertToFault(e, customConverters!, declaredFaultHandler);
                    }
                }

                // Not handled by a custom converter, so let's try the declared converters. 
                if (declaredFaultHandler.TryGetExceptionConverter(e, out var declaredConverters))
                {
                    TryConvertToFault(e, declaredConverters!, declaredFaultHandler);
                }
            }


            //    // Exception not handled by any custom or declared fault handler. Let's
            //    // perform default handling.
            //    if (e is RpcFaultException faultException)
            //    {
            //        rpcError = new RpcError { ErrorType = WellKnownRpcErrors.Fault, FaultCode = faultException.FaultCode, Message = faultException.Message };
            //    }
            //    else if (e is RpcFailureException)
            //    {
            //        rpcError = new RpcError { ErrorType = WellKnownRpcErrors.Failure, Message = e.Message };
            //    }
            //    else
            //    {
            //        // TODO: Log and/or call unhandled exception handler. This is an unexpected error that may be serious and 
            //        // should cause the server to shutdown.
            //        // Note, in case the server is shutdown, it would be very good if the response is sent to the client first (add 
            //        // suitable tests).

            //        //// TODO: Implement IncludeExceptionDetailInFaults
            //        //string message = "The server was unable to process the request due to an internal error. "
            //        //    + "For more information about the error, turn on IncludeExceptionDetailInFaults to send the exception information back to the client.";
            //        //rpcError = new RpcError { ErrorType = WellKnownRpcErrors.Fault, FaultCode = "", Message = message };
            //    }
            //}

            //this.Server.HandleError(rpcError);
            //return rpcError;
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

        internal static TimeSpan StreamingResponseWaitTime = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Forces a garbage collection before returning a weakly registered instance.
        /// <b>NOTE!</b> This may cause the service activation to become very slow.
        /// </summary>
        internal static bool ForceCollectActivatedInstance = false;
    }
}
