#region Copyright notice and license

// Copyright (c) 2019-2021, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License.
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//

#endregion Copyright notice and license

using SciTech.Collections;
using SciTech.ComponentModel;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Serialization;
using SciTech.Threading;
using System;
using System.Collections.Generic;
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

#pragma warning disable CA1062 // Validate arguments of public methods

    public abstract class RpcStub
    {
        protected RpcStub(IRpcServerCore server, ImmutableRpcServerOptions options)
        {
            this.Server = server;
            this.ServicePublisher = this.Server.ServicePublisher;

            this.AllowAutoPublish = options?.AllowAutoPublish ?? this.Server.AllowAutoPublish;
            this.Serializer = options?.Serializer ?? this.Server.Serializer;

            if (options != null && options.ExceptionConverters.Length > 0)
            {
                this.CustomFaultHandler = new RpcServerFaultHandler(this.Server.CustomFaultHandler, options.ExceptionConverters, null);
            }
            else
            {
                this.CustomFaultHandler = this.Server.CustomFaultHandler;
            }
        }

        public bool AllowAutoPublish { get; }

        public IRpcSerializer Serializer { get; }

        public IRpcServerCore Server { get; }

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
        private readonly IRpcServiceActivator serviceActivator;

        private readonly bool hasContextAccessor;

        public RpcStub(IRpcServerCore server, ImmutableRpcServerOptions options) : base(server, options)
        {
            this.serviceActivator = server.ServiceActivator;
            this.hasContextAccessor = server.HasContextAccessor;
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
                await eventProducer.Run(service.Value!).ContextFree();
                // TODO: Logger.Trace("EventProducer.Run returned successfully.");
            }
            catch (OperationCanceledException)
            {
                // TODO: Logger.Trace("EventProducer.Run cancelled.", oce);
                throw;
            }
            catch (Exception)
            {
                // TODO: Logger.Trace("EventProducer.Run error.", e);
                throw;
            }
            finally
            {
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <typeparam name="TRequest"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <typeparam name="TResponse"></typeparam>
        /// <param name="request"></param>
        /// <param name="context"></param>
        /// <param name="implCaller"></param>
        /// <returns></returns>
        public async ValueTask<RpcResponse<TResponse>> CallAsyncMethod<TRequest, TResult, TResponse>(
            TRequest request,
            IServiceProvider? serviceProvider,
            IRpcServerContextBuilder context,
            Func<TService, TRequest, CancellationToken, Task<TResult>> implCaller,
            Func<TResult, TResponse>? responseConverter,
            RpcServerFaultHandler? faultHandler,
            IRpcSerializer serializer) where TRequest : IObjectRequest
        {
            try
            {
                var (activatedService, interceptDisposables) = await this.BeginCall(serviceProvider, request.Id, context).ContextFree();

                try
                {
                    // Call the actual implementation method.
                    var result = await implCaller(activatedService.Value, request, context.CancellationToken).ContextFree();

                    context.CancellationToken.ThrowIfCancellationRequested();

                    return CreateResponse(responseConverter, result);
                }
                finally
                {
                    await this.EndCallAsync(activatedService, interceptDisposables).ContextFree();
                }
            }
            catch (Exception e)
            {
                this.HandleRpcError(e, faultHandler, serializer);
                throw;
            }
        }


        public async ValueTask<RpcResponse<TResponse>> CallBlockingMethod<TRequest, TResult, TResponse>(
            TRequest request,
            IRpcServerContextBuilder context,
            Func<TService, TRequest, CancellationToken, TResult> implCaller,
            Func<TResult, TResponse>? responseConverter,
            RpcServerFaultHandler? faultHandler,
            IRpcSerializer serializer,
            IServiceProvider? serviceProvider) where TRequest : IObjectRequest
        {
            try
            {
                var (activatedService, interceptDisposables) = await this.BeginCall(serviceProvider, request.Id, context).ContextFree();

                try
                {
                    // Call the actual implementation method.
                    var result = implCaller(activatedService.Value, request, context.CancellationToken);
                    context.CancellationToken.ThrowIfCancellationRequested();

                    return CreateResponse(responseConverter, result);
                }
                finally
                {
                    await this.EndCallAsync(activatedService, interceptDisposables).ContextFree();
                }
            }
            catch (Exception e)
            {
                this.HandleRpcError(e, faultHandler, serializer);
                throw;
            }
        }

        public async ValueTask CallServerStreamingMethod<TRequest, TResult, TResponse>(
            TRequest request,
            IServiceProvider? serviceProvider,
            IRpcServerContextBuilder context,
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
                        var result = implCaller(activatedService.Value, request, combinedCts.Token);
                        if( !activatedService.CanDispose )
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

                                    var response = CreateResponse(responseConverter, asyncEnum.Current);
                                    await responseWriter.WriteAsync(response.Result).ContextFree();
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

                        if (taskHasNext != null)
                        {
                            // Still waiting for next enumerator item so the enumerator cannot be disposed yet.
                            // Let's cancel the enumerator and dispose once finished. Let's not
                            // wait for cancellation to finish, since it may take awhile for the enumerator to notice it.
                            enumCts.Cancel();

                            taskHasNext.ContinueWith(t =>
                               {
                                   CleanupAsyncEnum().Forget();
                               }, TaskScheduler.Default).Forget();
                        }
                        else
                        {
                            await CleanupAsyncEnum().ContextFree();
                        }
                    }
                }
                finally
                {
                    await this.EndCallAsync(activatedService, interceptDisposables).ContextFree();
                }
            }
            catch (Exception e)
            {
                this.HandleRpcError(e, faultHandler, serializer);
                throw;
            }
        }

        //public async ValueTask<RpcResponse> CallVoidAsyncMethod<TRequest>(
        //    TRequest request,
        //    IServiceProvider? serviceProvider,
        //    IRpcServerContextBuilder context,
        //    Func<TService, TRequest, CancellationToken, Task> implCaller) where TRequest : IObjectRequest
        //{
        //    var (activatedService, interceptDisposables) = await this.BeginCall(serviceProvider, request.Id, context).ContextFree();

        //    try
        //    {
        //        // Call the actual implementation method.
        //        await implCaller(activatedService.Value, request, context.CancellationToken).ContextFree();
        //        context.CancellationToken.ThrowIfCancellationRequested();

        //        return new RpcResponse();
        //    }
        //    finally
        //    {
        //        await this.EndCallAsync(activatedService, interceptDisposables).ContextFree();
        //    }
        //}

        public async ValueTask<RpcResponse> CallVoidAsyncMethod<TRequest>(
            TRequest request,
            IServiceProvider? serviceProvider,
            IRpcServerContextBuilder context,
            Func<TService, TRequest, CancellationToken, Task> implCaller,
            RpcServerFaultHandler? faultHandler,
            IRpcSerializer serializer) where TRequest : IObjectRequest
        {
            try
            {
                var (activatedService, interceptDisposables) = await this.BeginCall(serviceProvider, request.Id, context).ContextFree();

                try
                {
                    // Call the actual implementation method.
                    await implCaller(activatedService.Value, request, context.CancellationToken).ContextFree();
                    context.CancellationToken.ThrowIfCancellationRequested();

                    return new RpcResponse();
                }
                finally
                {
                    await this.EndCallAsync(activatedService, interceptDisposables).ContextFree();
                }
            }
            catch (Exception e)
            {
                this.HandleRpcError(e, faultHandler, serializer);
                throw;
            }
        }

        //public async ValueTask<RpcResponse> CallVoidBlockingMethod<TRequest>(
        //    TRequest request,
        //    IServiceProvider? serviceProvider,
        //    IRpcServerContextBuilder context,
        //    Action<TService, TRequest, CancellationToken> implCaller)
        //    where TRequest : IObjectRequest
        //{
        //    var (activatedService, interceptDisposables) = await this.BeginCall(serviceProvider, request.Id, context).ContextFree();

        //    try
        //    {
        //        // Call the actual implementation method.
        //        implCaller(activatedService.Value, request, context.CancellationToken);
        //        context.CancellationToken.ThrowIfCancellationRequested();

        //        return new RpcResponse();
        //    }
        //    finally
        //    {
        //        await this.EndCallAsync(activatedService, interceptDisposables).ContextFree();
        //    }
        //}

        public async ValueTask<RpcResponse> CallVoidBlockingMethod<TRequest>(
            TRequest request,
            IServiceProvider? serviceProvider,
            IRpcServerContextBuilder context,
            Action<TService, TRequest, CancellationToken> implCaller,
            RpcServerFaultHandler? faultHandler,
            IRpcSerializer serializer)
            where TRequest : IObjectRequest
        {
            try
            {
                var (activatedService, interceptDisposables) = await this.BeginCall(serviceProvider, request.Id, context).ContextFree();

                try
                {
                    // Call the actual implementation method.
                    implCaller(activatedService.Value, request, context.CancellationToken);
                    context.CancellationToken.ThrowIfCancellationRequested();

                    return new RpcResponse();
                }
                finally
                {
                    await this.EndCallAsync(activatedService, interceptDisposables).ContextFree();
                }
            }
            catch (Exception e)
            {
                this.HandleRpcError(e, faultHandler, serializer);
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


        private ValueTask EndCallAsync(IOwned<TService>? activatedService, CompactList<IDisposable?> interceptDisposables)
        {
            ValueTask disposeTask = activatedService?.DisposeAsync() ?? default;
            if( disposeTask.IsCompletedSuccessfully)
            {
                if (this.hasContextAccessor)
                {
                    RpcContextAccessor.Clear();
                }

                for (int index = interceptDisposables.Count - 1; index >= 0; index--)
                {
                    try { interceptDisposables[index]?.Dispose(); } catch { /* TODO: Log? */ }
                }

                return default;
            } else
            {
                return AwaitDisposeAndFinalizeCallAsync();
            }

            async ValueTask AwaitDisposeAndFinalizeCallAsync()
            {
                await disposeTask.ContextFree();

                if (this.hasContextAccessor)
                {
                    RpcContextAccessor.Clear();
                }

                for (int index = interceptDisposables.Count - 1; index >= 0; index--)
                {
                    try { interceptDisposables[index]?.Dispose(); } catch { /* TODO: Log? */ }
                }
            }
        }

        private void CheckPublished(RpcObjectId objectId)
        {
            if (!this.serviceActivator.CanGetActivatedService<TService>(objectId))
            {
                throw CreateServiceUnavailableException(objectId);
            }
        }

        /// <summary>
        /// <para>
        /// Prepares a call to the RPC implementation method. Must be combined with a corresponding call to
        /// <see cref="EndCallAsync(in IOwned{TService}, CompactList{IDisposable?})"/>.
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
        private ValueTask<ValueTuple<IOwned<TService>, CompactList<IDisposable?>>> BeginCall(IServiceProvider? serviceProvider, RpcObjectId objectId, IRpcServerContextBuilder context)
        {
            var interceptors = this.Server.CallInterceptors;

            CompactList<IDisposable?> interceptDisposables = default;
            CompactList<Task<IDisposable>> pendingInterceptors = default;

            if (interceptors.Count > 0)
            {
                interceptDisposables.Reset(interceptors.Count);
            }

            try
            {
                for (int index = 0; index < interceptors.Count; index++)
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
                    if (this.hasContextAccessor)
                    {
                        RpcContextAccessor.Init(context);
                    }

                    var service = this.GetServiceImpl(serviceProvider, objectId);

                    return new ValueTask<(IOwned<TService>, CompactList<IDisposable?>)>((service, interceptDisposables));
                }

                return AwaitPendingInterceptors(pendingInterceptors, interceptDisposables,
                    serviceProvider, objectId,
                    this.hasContextAccessor ? context : null);
            }
            catch (Exception)
            {
                // An interceptor threw an exception. Try to cleanup and then rethrow.
                // TODO: Log.
                for (int index = interceptors.Count - 1; index >= 0; index--)
                {
                    try { interceptDisposables![index]?.Dispose(); } catch { }
                }

                throw;
            }

            async ValueTask<ValueTuple<IOwned<TService>, CompactList<IDisposable?>>> AwaitPendingInterceptors(
                CompactList<Task<IDisposable>> pendingInterceptors,
                CompactList<IDisposable?> interceptDisposables,
                IServiceProvider? serviceProvider, RpcObjectId objectId,
                IRpcServerContext? context)
            {
                var disposables = await Task.WhenAll(pendingInterceptors).ContextFree();
                interceptDisposables.AddRange(disposables);

                if (context != null)
                {
                    RpcContextAccessor.Init(context);
                }

                var service = this.GetServiceImpl(serviceProvider, objectId);

                return (service, interceptDisposables);
            }
        }

        private ValueTask<RpcResponse<TResponse>> CallCoreAsync<TRequest, TResult, TResponse>(
            TRequest request,
            IRpcServerContext context,
            Func<TService, TRequest, CancellationToken, TResult> implCaller,
            Func<TResult, TResponse>? responseConverter,
            RpcServerFaultHandler? faultHandler,
            IRpcSerializer serializer,
            in IOwned<TService> activatedService,
            CompactList<IDisposable?> interceptDisposables) where TRequest : IObjectRequest
        {
            try
            {
                try
                {
                    // Call the actual implementation method.
                    var result = implCaller(activatedService.Value, request, context.CancellationToken);
                    context.CancellationToken.ThrowIfCancellationRequested();

                    return new ValueTask<RpcResponse<TResponse>>(
                        CreateResponse(responseConverter, result));
                }
                finally
                {
                    ValueTask disposeTask = this.EndCallAsync(activatedService, interceptDisposables);
                    if( !disposeTask.IsCompletedSuccessfully)
                    {

                    }
                }
            }
            catch (Exception e)
            {
                this.HandleRpcError(e, faultHandler, serializer);
                throw;
            }
        }

        private IOwned<TService> GetServiceImpl(IServiceProvider? serviceProvider, RpcObjectId objectId)
        {            
            var activatedService = this.serviceActivator.GetActivatedService<TService>(serviceProvider, objectId);
            if (activatedService != null)
            {
                return activatedService;
            }

            throw CreateServiceUnavailableException(objectId);
        }

        private static RpcServiceUnavailableException CreateServiceUnavailableException(RpcObjectId objectId)
        {
            return new RpcServiceUnavailableException(objectId != RpcObjectId.Empty
                ? $"Service object '{objectId}' ({typeof(TService).Name}) not available."
                : $"Singleton service '{typeof(TService).Name}' not available.");
        }

        private void HandleRpcError(Exception e, RpcServerFaultHandler? declaredFaultHandler, IRpcSerializer serializer)
        {
            RpcFaultException? faultException = declaredFaultHandler?.TryCreateFaultException(e);

            if (faultException == null && this.CustomFaultHandler is RpcServerFaultHandler registeredFaultHandler)
            {
                faultException = registeredFaultHandler.TryCreateFaultException(e);
            }

            faultException ??= e as RpcFaultException;

            if (faultException != null && faultException.DetailsType is Type detailsType)
            {
                if (declaredFaultHandler == null || !declaredFaultHandler.IsFaultDeclared(faultException.FaultCode, detailsType))
                {
                    // Cannot use this one since the fault type is not declared or does not match.
                    // Let's strip away the fault details and throw a plain RpcFaultException.
                    faultException = new RpcFaultException(faultException.FaultCode, faultException.Message);
                }
            }

            //if (declaredFaultHandler != null)
            //{
            //    // Start by checking custom exception converters.
            //    if (this.CustomFaultHandler is RpcServerFaultHandler customFaultHandler)
            //    {
            //        if (customFaultHandler.TryGetExceptionConverter(e, out var customConverters))
            //        {
            //            // Using the methodStub fault handler as the faultHandler argument, since
            //            // this faultHandler is used to check whether the fault is declared for the specific operation.
            //            faultException = TryConvertToFault(e, customConverters!, declaredFaultHandler);
            //        }
            //    }

            //    if (faultException == null)
            //    {
            //        // Not handled by a custom converter, so let's try the declared converters.
            //        if (declaredFaultHandler.TryGetExceptionConverter(e, out var declaredConverters))
            //        {
            //            TryConvertToFault(e, declaredConverters!, declaredFaultHandler);
            //        }
            //    }
            //}

            // Give the server an oppurtunity to convert the exception to a suitable type
            // to be "returned" to client.
            this.Server.HandleCallException(faultException ?? e, serializer);

            if (faultException != null)
            {
                throw faultException;
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
        /// <b>NOTE!</b> This may cause the service activation to become very slow. Should only be set to true when running tests.
        /// </summary>
        internal static bool ForceCollectActivatedInstance = false;
    }
}