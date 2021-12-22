#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using Microsoft.Extensions.Logging;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Logging;
using SciTech.Rpc.Serialization;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Client.Internal
{
    /// <summary>
    /// 
    /// </summary>
    public class RpcProxyArgs
    {
        public RpcProxyArgs(
            IRpcChannel connection, RpcObjectId objectId, IRpcSerializer serializer,
            IReadOnlyCollection<string>? implementedServices,
            SynchronizationContext? syncContext)
        {
            this.Channel = connection ?? throw new ArgumentNullException(nameof(connection));
            this.ObjectId = objectId;
            this.Serializer = serializer;
            this.ImplementedServices = implementedServices;
            this.SyncContext = syncContext;
        }

        public IRpcChannel Channel { get; }

        /// <summary>
        /// The services implemented by the server side of this proxy. May be <c>null</c> or empty if
        /// implemented services are not known.
        /// </summary>
        public IReadOnlyCollection<string>? ImplementedServices { get; }

        public RpcObjectId ObjectId { get; }

        public IRpcSerializer Serializer { get; }

        public SynchronizationContext? SyncContext { get; }
    }

    [SuppressMessage("Naming", "CA1708: Identifiers should differ by more than case", Justification = "Accessed by generated code")]
    public abstract class RpcProxyBase
    {
        private protected readonly ILogger? logger;

        protected RpcProxyBase(RpcProxyArgs proxyArgs)
        {
            if (proxyArgs is null) throw new ArgumentNullException(nameof(proxyArgs));

            this.objectId = proxyArgs.ObjectId;
            this.Channel = proxyArgs.Channel;
            this.serializer = proxyArgs.Serializer;
            this.SyncContext = proxyArgs.SyncContext;
            if (proxyArgs.ImplementedServices?.Count > 0)
            {
                if (proxyArgs.ImplementedServices is HashSet<string> hashedServices)
                {
                    this.implementedServices = hashedServices;
                }
                else
                {
                    this.implementedServices = new HashSet<string>(proxyArgs.ImplementedServices);
                }
            }

            this.logger = /*proxyArgs.Logger ?? */ RpcLogger.TryCreateLogger(this.GetType());
        }

        public IRpcChannel Channel { get; }

        /// <summary>
        /// The services implemented by the server side this proxy. May be empty if
        /// implemented services are not known.
        /// </summary>
        public IReadOnlyCollection<string> ImplementedServices => this.implementedServices ?? (IReadOnlyCollection<string>)Array.Empty<string>();

        public RpcObjectId ObjectId => this.objectId;

        public SynchronizationContext? SyncContext { get; }

        protected object SyncRoot { get; } = new object();

        public bool ImplementsServices(IReadOnlyCollection<string>? otherServices)
        {
            return otherServices == null || otherServices.Count == 0
                || (this.implementedServices != null && this.implementedServices.IsSupersetOf(otherServices));
        }


        /// <summary>
        /// Protected to make it easier to use by dynamically generated code.
        /// </summary>
        [SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Accessed by generated code")]
        protected readonly RpcObjectId objectId;

        /// <summary>
        /// Protected to make it easier to use by dynamically generated code.
        /// </summary>
        [SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Accessed by generated code")]
        protected readonly IRpcSerializer serializer;

        private HashSet<string>? implementedServices;

    }

    /// <summary>
    /// Base implementation of an RPC proxy. Derived classes must, in addition to implementing the abstract methods, also 
    /// include a static method named "CreateMethodDef", with the signature 
    /// TMethodDef CreateMethodDef{TRequest, TResponse}(RpcMethodType,string,string,IRpcSerializer?,RpcClientFaultHandler?).
    /// TODO: Try to implement the "CreateMethodDef" functionality using a virtual method in <see cref="RpcProxyGenerator{TRpcProxy, TProxyArgs, TMethodDef}"/>
    /// instead.
    /// </summary>
    /// <typeparam name="TMethodDef"></typeparam>
    public abstract class RpcProxyBase<TMethodDef> : RpcProxyBase, IRpcProxy where TMethodDef : RpcProxyMethod
    {

        internal const string AddEventHandlerAsyncName = nameof(AddEventHandlerAsync);

        internal const string CallAsyncEnumerableMethodName = nameof(CallAsyncEnumerableMethod);

        internal const string CallCallbackMethodAsyncName = nameof(CallCallbackMethodAsync);

        internal const string CallCallbackMethodName = nameof(CallCallbackMethod);

        internal const string CallUnaryMethodAsyncName = nameof(CallUnaryMethodAsync);

        internal const string CallUnaryMethodName = nameof(CallUnaryMethod);

        internal const string CallUnaryVoidMethodAsyncName = nameof(CallUnaryVoidMethodAsync);

        internal const string CallUnaryVoidMethodName = nameof(CallUnaryVoidMethod);

        // There's actually no method called CreateMethodDef in this class. It should be defined as
        // a static method on the class implementing RpcProxyBase.
        internal const string CreateMethodDefName = "CreateMethodDef";

        internal const string ObjectIdFieldName = nameof(objectId);

        internal const string ProxyMethodsFieldName = nameof(proxyMethods);

        internal const string RemoveEventHandlerAsyncName = nameof(RemoveEventHandlerAsync);

        [SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Accessed by generated code")]
        protected internal readonly TMethodDef[] proxyMethods;

        // private static readonly ILog Logger = LogProvider.For<RpcProxyBase<TMethodDef>>();

        private readonly List<Task> pendingEventTasks = new List<Task>();

        private Dictionary<int, EventData>? activeEvents;

        private HashSet<string>? implementedServices;

        private TMethodDef? queryServicesMethodDef;

        private TaskCompletionSource<HashSet<string>>? servicesTcs;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="proxyArgs"></param>
        /// <param name="proxyMethods"></param>
        protected RpcProxyBase(RpcProxyArgs proxyArgs, TMethodDef[] proxyMethods) : base(proxyArgs)
        {
            this.proxyMethods = proxyMethods;
        }

        public event EventHandler? EventHandlerFailed;

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        public bool Equals([AllowNull]IRpcProxy other)
        {
            return other != null && other.ObjectId == this.objectId;
        }

        public override bool Equals(object? obj)
        {
            return obj is IRpcProxy other && this.Equals(other);
        }

        public override int GetHashCode()
        {
            return this.objectId.GetHashCode();
        }

        public async Task<HashSet<string>> GetImplementedServicesAsync()
        {
            TaskCompletionSource<HashSet<string>>? newServicesTcs = null;
            Task<HashSet<string>>? currServicesTask = null;

            lock (this.SyncRoot)
            {
                if (this.implementedServices != null)
                {
                    return this.implementedServices;
                }

                if (this.servicesTcs == null)
                {
                    newServicesTcs = this.servicesTcs = new TaskCompletionSource<HashSet<string>>();
                }
                else
                {
                    currServicesTask = this.servicesTcs.Task;
                }
            }

            if (currServicesTask != null)
            {
                return await currServicesTask.ConfigureAwait(false);
            }


            if (this.queryServicesMethodDef == null)
            {
                // Don't care if it's created multiple times, it's just a small data class
                this.queryServicesMethodDef = this.CreateDynamicMethodDef<RpcObjectRequest, RpcServicesQueryResponse>(
                    "SciTech.Rpc.RpcService", "QueryServices");
            }

            var servicesResponse = await this.CallUnaryMethodCoreAsync<RpcObjectRequest, RpcServicesQueryResponse>(
                this.queryServicesMethodDef,
                new RpcObjectRequest(this.objectId),
                CancellationToken.None).ContextFree();

            var implementedServices = new HashSet<string>(servicesResponse.ImplementedServices);
            lock (this.SyncRoot)
            {
                this.implementedServices = implementedServices;
                this.servicesTcs = null;
            }

            newServicesTcs!.SetResult(implementedServices);

            return implementedServices;
        }

        /// <summary>
        /// <para>
        /// Tries to cast this proxy to a type that implements the service <typeparamref name="TService"/>. 
        /// </para>
        /// <para>
        /// NOTE. If information about implemented services are not available for this proxy, 
        /// a remote call to the RPC server will be performed to retrieve this information.</para>
        /// </summary>
        /// <typeparam name="TService"></typeparam>
        /// <returns></returns>
        public async Task<TService?> TryCastAsync<TService>() where TService : class
        {
            string serviceName = GetServiceName<TService>();

            HashSet<string> implementedServices = await this.GetImplementedServicesAsync().ContextFree();

            if (implementedServices.Contains(serviceName))
            {
                return UnsafeCast<TService>();
            }

            return null;
        }

        public TService UnsafeCast<TService>() where TService : class
        {
            return this.Channel.GetServiceInstance<TService>(this.objectId, this.implementedServices, this.SyncContext);
        }

        public Task WaitForPendingEventHandlersAsync()
        {
            Task[] pendingEventTasksCopy;
            lock (this.SyncRoot)
            {
                pendingEventTasksCopy = this.pendingEventTasks.ToArray();
            }

            return Task.WhenAll(pendingEventTasksCopy);
        }

        protected Task AddEventHandlerAsync<TEventHandler, TEventArgs>(
            TEventHandler value, int eventMethodIndex)
            where TEventArgs : class
            where TEventHandler : Delegate
        {
            Task addTask;

            EventData<TEventHandler>? newEventData = null;
            lock (this.SyncRoot)
            {
                EventData<TEventHandler> eventData;

                var activeEventData = this.GetEventDataSynchronized<TEventHandler>(eventMethodIndex);
                if (activeEventData != null)
                {
                    Debug.Assert(activeEventData.eventHandler != null);
                    eventData = activeEventData;
                    addTask = activeEventData.eventListenerStartedTcs.Task;
                }
                else
                {
                    newEventData = this.CreateEventDataSynchronized<TEventHandler>(eventMethodIndex);
                    Debug.Assert(newEventData.eventHandler == null);
                    var finishedTask = newEventData.eventListenerFinishedTcs!.Task;
                    finishedTask.ContinueWith(t => { lock (this.SyncRoot) { this.RemoveEventDataSynchronized(newEventData); } }, TaskScheduler.Default);

                    eventData = newEventData;
                }

                addTask = eventData.eventListenerStartedTcs!.Task;
                eventData.eventHandler = (TEventHandler)Delegate.Combine(eventData.eventHandler, value);
            }

            if (newEventData != null)
            {
                this.AddPendingEventTask(newEventData.eventListenerStartedTcs.Task);

                // New event data indicates that the first event handler was added,
                // and we need to start retrieving event callbacks.
                // StartEventRetriever will handle the task completion sources.

                this.StartEventRetrieverAsync<TEventHandler, TEventArgs>(newEventData);

            }

            return addTask;
        }

        protected async IAsyncEnumerable<TReturn> CallAsyncEnumerableMethod<TRequest, TResponseReturn, TReturn>(
            TMethodDef method,
            TRequest request,
            Func<IRpcProxy, object?, object?> responseConverter,
            [EnumeratorCancellation]CancellationToken cancellationToken)
            where TRequest : class
            where TResponseReturn : class
        {
            var streamingCall = await this.CallStreamingMethodAsync<TRequest, TResponseReturn>(request, method, cancellationToken).ContextFree();
            await using (streamingCall.ContextFree())
            {
                var sequence = streamingCall.ResponseStream;
                while (true)
                {
                    TReturn retVal;

                    try
                    {
                        if (await sequence.MoveNextAsync().ContextFree())
                        {
                            retVal = this.ConvertResult<TResponseReturn, TReturn>(responseConverter, sequence.Current);
                        }
                        else
                        {
                            break;
                        }
                    }
                    catch (Exception e)
                    {
                        this.HandleCallException(method, e);
                        throw;
                    }

                    yield return retVal;
                }
            }
        }

        protected void CallCallbackMethod<TRequest, TResponseReturn, TReturn>(
            TMethodDef method,
            TRequest request,
            Action<TReturn> callback,
            Func<IRpcProxy, object?, object?> responseConverter,
            CancellationToken cancellationToken)
            where TRequest : class
            where TResponseReturn : class
        {
            CallCallbackMethodAsync<TRequest, TResponseReturn, TReturn>(method, request, callback, responseConverter, cancellationToken).AwaiterResult();
        }


        protected async Task CallCallbackMethodAsync<TRequest, TResponseReturn, TReturn>(
            TMethodDef method,
            TRequest request,
            Action<TReturn> callback,
            Func<IRpcProxy, object?, object?> responseConverter,
            CancellationToken cancellationToken)
                where TRequest : class
                where TResponseReturn : class
        {
            var streamingCall = await this.CallStreamingMethodAsync<TRequest, TResponseReturn>(request, method, cancellationToken).ContextFree();
            await using (streamingCall.ContextFree())
            {
                var sequence = streamingCall.ResponseStream;
                SendOrPostCallback? postCallback = null;

                if (callback != null && this.SyncContext != null)
                {
                    postCallback = s => callback((TReturn)s!);
                } 

                while (true)
                {
                    TReturn retVal;

                    try
                    {
                        if (await sequence.MoveNextAsync().ContextFree())
                        {
                            retVal = this.ConvertResult<TResponseReturn, TReturn>(responseConverter, sequence.Current);
                        }
                        else
                        {
                            break;
                        }
                    }
                    catch (Exception e)
                    {
                        this.HandleCallException(method, e);
                        throw;
                    }

                    if (postCallback != null)
                    {
                        this.SyncContext!.Post(postCallback, retVal);
                    }
                    else
                    {
                        callback?.Invoke(retVal);
                    }
                }

            }
        }


        protected abstract ValueTask<IAsyncStreamingServerCall<TResponse>> CallStreamingMethodAsync<TRequest, TResponse>(TRequest request, TMethodDef method, CancellationToken cancellationToken)
            where TRequest : class
            where TResponse : class;

        protected TReturnType CallUnaryMethod<TRequest, TResponseType, TReturnType>(
            TMethodDef methodDef,
            TRequest request,
            Func<IRpcProxy, object?, object?> responseConverter,
            CancellationToken cancellationToken)
            where TRequest : class
        {
            if (methodDef is null) throw new ArgumentNullException(nameof(methodDef));

            RpcResponse<TResponseType> response;
            try
            {
                response = this.CallUnaryMethodCore<TRequest, RpcResponse<TResponseType>>(methodDef, request, cancellationToken);
            }
            catch (Exception e)
            {
                this.HandleCallException(methodDef, e);
                throw;
            }

            return this.ConvertResult<TResponseType, TReturnType>(responseConverter, response.Result);
        }

        protected async Task<TReturnType> CallUnaryMethodAsync<TRequest, TResponseType, TReturnType>(
            TMethodDef methodDef,
            TRequest request,
            Func<IRpcProxy, object?, object?> responseConverter,
            CancellationToken cancellationToken)
            where TRequest : class
        {
            if (methodDef is null) throw new ArgumentNullException(nameof(methodDef));

            RpcResponse<TResponseType> response;
            try
            {
                response = await this.CallUnaryMethodCoreAsync<TRequest, RpcResponse<TResponseType>>(methodDef, request, cancellationToken).ContextFree();
            }
            catch (Exception e)
            {
                this.HandleCallException(methodDef, e);
                throw;
            }

            return this.ConvertResult<TResponseType, TReturnType>(responseConverter, response.Result);
        }


        protected abstract TResponse CallUnaryMethodCore<TRequest, TResponse>(TMethodDef methodDef, TRequest request, CancellationToken cancellationToken)
            where TRequest : class
            where TResponse : class;

        protected abstract Task<TResponse> CallUnaryMethodCoreAsync<TRequest, TResponse>(TMethodDef methodDef, TRequest request, CancellationToken cancellationToken)
            where TRequest : class
            where TResponse : class;



        protected void CallUnaryVoidMethod<TRequest>(TMethodDef methodDef, TRequest request, CancellationToken cancellationToken)
            where TRequest : class
        {
            if (methodDef is null) throw new ArgumentNullException(nameof(methodDef));

            try
            {
                this.CallUnaryMethodCore<TRequest, RpcResponse>(methodDef, request, cancellationToken);
            }
            catch (Exception e)
            {
                this.HandleCallException(methodDef, e);
                throw;
            }
        }

        protected async Task CallUnaryVoidMethodAsync<TRequest>(TMethodDef methodDef, TRequest request, CancellationToken cancellationToken)
            where TRequest : class
        {
            if (methodDef is null) throw new ArgumentNullException(nameof(methodDef));

            try
            {
                await this.CallUnaryMethodCoreAsync<TRequest, RpcResponse>(methodDef, request, cancellationToken).ContextFree();
            }
            catch (Exception e)
            {
                this.HandleCallException(methodDef, e);
                throw;
            }
        }


        protected abstract TMethodDef CreateDynamicMethodDef<TRequest, TResponse>(string serviceName, string operationName)
            where TRequest : class
            where TResponse : class;

        protected virtual void Dispose(bool disposing)
        {
            this.ClearEventHandlers();
            // TODO: Use IAsyncDisposable when available.
            this.WaitForPendingEventHandlersAsync().AwaiterResult();
            // TODO: Dispose (and end) owning RPC call.
        }

        protected abstract void HandleCallException(TMethodDef methodDef, Exception e);

        protected virtual bool IsCancellationException(Exception exception)
        {
            return exception is OperationCanceledException;
        }

        protected async Task RemoveEventHandlerAsync<TEventHandler, TEventArgs>(TEventHandler value, int eventMethodIndex)
            where TEventArgs : class
            where TEventHandler : Delegate
        {
            RpcProxyBase<TMethodDef>.EventData<TEventHandler>? removedEventData = this.RemoveEventHandler(value, eventMethodIndex);

            if (removedEventData != null)
            {
                removedEventData.cancellationSource.Cancel();
                try
                {
                    var finishedTask = removedEventData.eventListenerFinishedTcs.Task;
                    this.AddPendingEventTask(finishedTask);
                    await finishedTask.ContextFree();
                }
                catch (Exception x1) when (this.IsCancellationException(x1))
                {
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch (Exception)
                {
                    // TODO: Logger.Warn(x2, "Error when removing event handler.");
                }
#pragma warning restore CA1031 // Do not catch general exception types
            }
            else
            {
                return;
            }
        }

        private static string GetServiceName<TService>() where TService : class
        {
            // TODO: Cache
            var serviceInfo = RpcBuilderUtil.GetServiceInfoFromType(typeof(TService));
            return serviceInfo.FullName;
        }

        private void AddPendingEventTask(Task task)
        {
            lock (this.SyncRoot)
            {
                this.pendingEventTasks.Add(task);
            }

            task.ContinueWith(t =>
            {
                lock (this.SyncRoot)
                {
                    this.pendingEventTasks.Remove(t);
                }
            }, TaskScheduler.Default);
        }

        private void ClearEventHandlers()
        {
            List<EventData>? eventsCopy = null;
            lock (this.SyncRoot)
            {
                if (this.activeEvents != null)
                {
                    eventsCopy = this.activeEvents.Values.ToList();
                    this.activeEvents = null;

                }
            }

            if (eventsCopy != null)
            {
                foreach (var eventData in eventsCopy)
                {
                    if (eventData != null && eventData.Clear())
                    {
                        eventData.cancellationSource.Cancel();

                        var finishedTask = eventData.eventListenerFinishedTcs.Task;
                        this.AddPendingEventTask(finishedTask);
                    }
                }
            }
        }

        private TReturnType ConvertResult<TResponseType, TReturnType>(Func<IRpcProxy, object?, object?>? responseConverter, TResponseType result)
        {
            if (responseConverter != null)
            {
                return (TReturnType)responseConverter(this, result)!;
            }

            if (result is TReturnType returnValue)
            {
                return returnValue;
            }

            return default!;
        }

        private EventData<TEventHandler> CreateEventDataSynchronized<TEventHandler>(int methodIndex) where TEventHandler : class, Delegate
        {
            if (this.activeEvents == null)
            {
                this.activeEvents = new Dictionary<int, EventData>();
            }

            if (this.activeEvents.TryGetValue(methodIndex, out var eventData))
            {
                return (EventData<TEventHandler>)eventData;
            }

            var newEventData = new EventData<TEventHandler>(methodIndex);
            this.activeEvents.Add(methodIndex, newEventData);
            return newEventData;
        }

        private EventData<TEventHandler>? GetEventDataSynchronized<TEventHandler>(int methodIndex) where TEventHandler : class, Delegate
        {
            if (this.activeEvents != null && this.activeEvents.TryGetValue(methodIndex, out var eventData))
            {
                return (EventData<TEventHandler>)eventData;
            }

            return null;
        }

        protected void HandleRpcError(TMethodDef methodDef, RpcError error)
        {
            if (methodDef is null) throw new ArgumentNullException(nameof(methodDef));
            if (error is null) throw new ArgumentNullException(nameof(error));

            string message = error.Message ?? $"Error occured in server handler of 'xxx'";// {methodDef.Operation}'";
            switch (error.ErrorType)
            {
                case WellKnownRpcErrors.ServiceUnavailable:
                    throw new RpcServiceUnavailableException(message);
                case WellKnownRpcErrors.Failure:
                    throw new RpcFailureException(RpcFailureException.GetFailureFromFaultCode(error.ErrorCode), message);
                case WellKnownRpcErrors.Fault:
                    // Just leave switch and handle fault below.
                    break;
                default:
                    throw new RpcFailureException(RpcFailure.Unknown, $"Operation returned an unknown error of type '{error.ErrorType}'. {message}");
            }

            var actualSerializer = methodDef.SerializerOverride ?? this.serializer;
            var faultException = methodDef.FaultHandler.CreateFaultException(error, actualSerializer);
            var convertedException = methodDef.FaultHandler.TryConvertException(faultException);
            if( convertedException == null )
            {
                // Could not be converted by a declared converter, but maybe a registered converter can.
                // TODO: Currently there's no support for service level exception converters (as exists on the server side).
                foreach( var converter in this.Channel.Options.ExceptionConverters)
                {
                    convertedException = converter.TryCreateException(faultException);
                    if( convertedException != null )
                    {
                        break;
                    }
                }
            }

            throw convertedException ?? faultException;
        }



        private void InvokeDelegate<TEventHandler, TEventArgs>(TEventHandler eventHandler, TEventArgs eventArgs)
            where TEventHandler : Delegate
            where TEventArgs : class
        {
            if (eventHandler is EventHandler<TEventArgs> genericHandler)
            {
                genericHandler.Invoke(this, eventArgs);
            }
            else if (eventHandler is EventHandler plainHandler)
            {
                plainHandler.Invoke(this, (eventArgs as EventArgs)!);
            }
            else
            {
                Debug.Fail("RPC events only support EventHandler and EventHandler<>.");
            }
        }

        private bool IsEventActiveSynchronized(EventData eventData)
        {
            return this.activeEvents != null
                && this.activeEvents.TryGetValue(eventData.eventMethodIndex, out var activeEventData)
                && activeEventData == eventData;
        }


        private void RemoveEventDataSynchronized(EventData eventData)
        {
            if (!eventData.isRemoved)
            {
                eventData.isRemoved = true;
                this.activeEvents?.Remove(eventData.eventMethodIndex);
            }
        }

        private RpcProxyBase<TMethodDef>.EventData<TEventHandler>? RemoveEventHandler<TEventHandler>(TEventHandler value, int eventMethodIndex) where TEventHandler : Delegate
        {
            EventData<TEventHandler>? removedEventData = null;
            lock (this.SyncRoot)
            {
                var eventData = GetEventDataSynchronized<TEventHandler>(eventMethodIndex);
                if (eventData?.eventHandler != null)
                {
                    eventData.eventHandler = (TEventHandler?)Delegate.Remove(eventData.eventHandler, value);
                    if (eventData.eventHandler == null)
                    {
                        this.RemoveEventDataSynchronized(eventData);
                        removedEventData = eventData;
                    }
                }
            }

            return removedEventData;
        }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Exceptions reported using EventHandlerFailed")]
        private async void StartEventRetrieverAsync<TEventHandler, TEventArgs>(EventData<TEventHandler> eventData)
            where TEventArgs : class
            where TEventHandler : Delegate
        {
            var request = new RpcObjectRequest(this.objectId);

            try
            {
                var beginMethod = this.proxyMethods[eventData.eventMethodIndex];
                var streamingCallTask = this.CallStreamingMethodAsync<RpcObjectRequest, TEventArgs>(
                    request, beginMethod, eventData.cancellationSource.Token);

                var streamingCall = await streamingCallTask.ContextFree();
                await using (streamingCall.ContextFree())
                {
                    var responseStream = streamingCall.ResponseStream;

                    // The event producer operation will return an initial empty EventArgs, to
                    // allow the client (us) to know that the event handler has been properly added 
                    // on the server side.
                    // The empty EventArgs is just ignored.
                    await responseStream.MoveNextAsync().ContextFree();

                    // Mark the listener as completed once we have received the initial EventArgs
                    eventData.eventListenerStartedTcs.SetResult(true);

                    while (await responseStream.MoveNextAsync().ContextFree())
                    {
                        TEventHandler? eventHandler = null;
                        lock (this.SyncRoot)
                        {
                            // Make sure that we're still the active event retriever.
                            if (this.IsEventActiveSynchronized(eventData))
                            {
                                eventHandler = eventData.eventHandler;
                            }
                            else
                            {
                                // TODO: Log
                            }
                        }

                        if (eventHandler != null)
                        {
                            var eventArgs = responseStream.Current;
                            if (this.SyncContext != null)
                            {
                                this.SyncContext.Post(s =>
                                {
                                    try
                                    {
                                        this.InvokeDelegate(eventHandler, eventArgs);
                                    }
                                    catch( Exception x )
                                    {
                                        NotifyEventListenerFailed(x);
                                        eventData.cancellationSource.Cancel();
                                    }
                                }, null);
                            }
                            else
                            {
                                this.InvokeDelegate(eventHandler, eventArgs);
                            }
                        }
                    }
                }

                eventData.eventListenerFinishedTcs.TrySetResult(true);
            }
            catch (Exception ce) when (this.IsCancellationException(ce))
            {
                if (eventData.cancellationSource.IsCancellationRequested)
                {
                    // If cancellation has been requested, let's just
                    // silently end the retriever

                    eventData.eventListenerStartedTcs.TrySetResult(false);

                    eventData.eventListenerFinishedTcs.TrySetResult(false);
                }
                else
                {
                    // This is most likely some error, the
                    // retrieve loop should only be cancelled through
                    // eventData.cancellationSource (e.g. NetGrpc returns cancelled
                    // when streaming call fails due to authorization error).

                    NotifyEventListenerFailed(ce);
                }
            }
            catch (Exception e)
            {
                NotifyEventListenerFailed(e);
            }

            void NotifyEventListenerFailed(Exception e) 
            {
                // Try to set exception on started task as well, in case 
                // the exception occurred while starting.
                eventData.eventListenerStartedTcs.TrySetException(e);

                eventData.eventListenerFinishedTcs.TrySetException(e);

                this.EventHandlerFailed?.Invoke(this, new ExceptionEventArgs(e));
            }
        }



        protected internal sealed class EventData<TEventHandler> : EventData where TEventHandler : class, Delegate
        {
            internal TEventHandler? eventHandler;

            public EventData(int eventMethodIndex) : base(eventMethodIndex)
            {

            }

            internal override bool Clear()
            {
                if (this.eventHandler != null)
                {
                    this.eventHandler = null;
                    return true;
                }

                return false;
            }
        }

#pragma warning disable CA1001 // Types that own disposable fields should be disposable
        protected internal abstract class EventData
        {
            internal readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();

            internal readonly TaskCompletionSource<bool> eventListenerFinishedTcs = new TaskCompletionSource<bool>();

            internal readonly TaskCompletionSource<bool> eventListenerStartedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            internal readonly int eventMethodIndex;

            internal bool isRemoved;

            protected EventData(int eventMethodIndex)
            {
                this.eventMethodIndex = eventMethodIndex;
            }

            internal abstract bool Clear();
        }
#pragma warning restore CA1001 // Types that own disposable fields should be disposable

    }

    //#pragma warning restore CA1031 // Do not catch general exception types
    //#pragma warning restore CA1051 // Do not declare visible instance fields
    //#pragma warning restore CA1062 // Validate arguments of public methods

    public class RpcProxyMethodsCache<TMethodDef>
    {
        private readonly Func<IRpcSerializer, TMethodDef[]> proxyMethodsCreator;

        private readonly ConditionalWeakTable<IRpcSerializer, TMethodDef[]> SerializerToProxyMethods = new ConditionalWeakTable<IRpcSerializer, TMethodDef[]>();

        private readonly object syncRoot = new object();

        public RpcProxyMethodsCache(Func<IRpcSerializer, TMethodDef[]> proxyMethodsCreator)
        {
            this.proxyMethodsCreator = proxyMethodsCreator;
        }

        public TMethodDef[] GetProxyMethods(IRpcSerializer serializer)
        {
            lock (this.syncRoot)
            {
                if (this.SerializerToProxyMethods.TryGetValue(serializer, out var existingProxyMethods))
                {
                    return existingProxyMethods;
                }

                var proxyMethods = this.proxyMethodsCreator(serializer);
                this.SerializerToProxyMethods.Add(serializer, proxyMethods);

                return proxyMethods;
            }
        }
    }

    /// <summary>
    /// Contains global proxy options, mainly intended for testing.
    /// </summary>
    internal static class RpcProxyOptions
    {
        /// <summary>
        /// Indicates that cancellations and timeouts should round-trip to the server
        /// before being completed.
        /// Should only be set to true when running tests.
        /// </summary>
        internal static bool RoundTripCancellationsAndTimeouts;
    }

    public static class ServiceConverter<TService> where TService : class
    {
        public static readonly Func<IRpcProxy, object?, object?> Default = (proxy, input) =>
        {
            if (input is RpcObjectRef serviceRef)
            {
                return proxy.Channel.GetServiceInstance<TService>(serviceRef, proxy.SyncContext);
            }

            if (input != null)
            {
                throw new ArgumentException("Invalid input", nameof(input));
            }

            return null;
        };

        public static readonly Func<IRpcProxy, object?, object?> DefaultArray = (proxy, input) =>
        {
            if (input is RpcObjectRef[] serviceRefs)
            {
                var services = new TService?[serviceRefs.Length];

                var connection = proxy.Channel;
                for (int i = 0; i < services.Length; i++)
                {
                    var serviceRef = serviceRefs[i];
                    services[i] = serviceRef != null ? connection.GetServiceInstance<TService>(serviceRef, proxy.SyncContext) : null;
                }

                return services;
            }

            if (input != null)
            {
                throw new ArgumentException("Invalid input", nameof(input));
            }

            return null;
        };
    }

    public static class ServiceRefConverter<TService> where TService : class
    {
        public static readonly Func<IRpcProxy, object?, object?> Default = (proxy, input) =>
            ((RpcObjectRef?)input)?.Cast<TService>();

        public static readonly Func<IRpcProxy, object?, object?> DefaultArray = (proxy, input) =>
        {
            if (input is RpcObjectRef[] serviceRefs)
            {
                var typedServiceRefs = new RpcObjectRef<TService>?[serviceRefs.Length];

                _ = proxy.Channel;
                for (int i = 0; i < typedServiceRefs.Length; i++)
                {
                    var serviceRef = serviceRefs[i];
                    typedServiceRefs[i] = serviceRef?.Cast<TService>();
                }

                return typedServiceRefs;
            }

            if (input != null)
            {
                throw new ArgumentException("Invalid input", nameof(input));
            }

            return null;
        };
    }
}
