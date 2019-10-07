#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//

//
// Based on SimplPipeline in SimplPipelines/SimplSockets by Marc Gravell (https://github.com/mgravell/simplsockets)
//
#endregion

using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Logging;
using SciTech.Rpc.Serialization;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Client.Internal
{
    internal class RpcPipelineClient : RpcPipeline
    {
        private static readonly ILog Logger = LogProvider.For<RpcPipelineClient>();
        
        private static readonly bool TraceEnabled = Logger.IsTraceEnabled();

        private readonly Dictionary<int, IResponseHandler> awaitingResponses
            = new Dictionary<int, IResponseHandler>();

        private int nextMessageId;

        private Task? receiveLoopTask;

        public RpcPipelineClient(IDuplexPipe pipe, int? maxRequestSize, int? maxResponseSize, bool skipLargeFrames)
            : base(pipe, maxRequestSize, maxResponseSize, skipLargeFrames)
        {
        }

        private interface IResponseHandler
        {
            void HandleCancellation();

            void HandleError(Exception ex);

            bool HandleResponse(LightweightRpcFrame frame);
        }

        public Task AwaitFinished()
        {
            return this.receiveLoopTask ?? Task.CompletedTask;
        }

#pragma warning disable CA2000 // Dispose objects before losing scope
        public async ValueTask<IAsyncStreamingServerCall<TResponse>> BeginStreamingServerCall<TRequest, TResponse>(
            RpcFrameType frameType,
            string operation,
            IReadOnlyCollection<KeyValuePair<string, string>>? headers,
            TRequest request,
            LightweightSerializers<TRequest, TResponse> serializers,
            int callTimeout,
            CancellationToken cancellationToken)
            where TRequest : class
            where TResponse : class
        {
            var streamingCall = new AsyncStreamingServerCall<TResponse>(serializers.ResponseSerializer);
            int messageId = this.AddAwaitingResponse(streamingCall);

            try
            {
                RpcOperationFlags flags = 0;
                if (cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(() => this.CancelCall(messageId, operation));
                    flags |= RpcOperationFlags.CanCancel;
                }

                if (callTimeout > 0 && !RpcProxyOptions.RoundTripCancellationsAndTimeouts)
                {
                    Task.Delay(callTimeout)
                        .ContinueWith(t => this.TimeoutCall(messageId, operation, callTimeout), cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Forget();
                }

                var frame = new LightweightRpcFrame(frameType, messageId, operation, flags, (uint)callTimeout, headers);

                var payloadStream = await this.BeginWriteAsync(frame).ContextFree();
                this.WriteRequest(request, serializers.RequestSerializer, payloadStream);

                return streamingCall;
            }
            catch (Exception e)
            {
                this.HandleCallError(messageId, e);
                throw;
            }
        }

#pragma warning restore CA2000 // Dispose objects before losing scope


        public void RunAsyncCore(CancellationToken cancellationToken = default)
        {
            if (this.receiveLoopTask != null)
            {
                throw new InvalidOperationException("Receive loop is already running.");
            }

            this.receiveLoopTask = this.StartReceiveLoopAsync(cancellationToken);
        }

#pragma warning disable CA2000 // Dispose objects before losing scope
        public Task<TResponse> SendReceiveFrameAsync<TRequest, TResponse>(
            RpcFrameType frameType,
            string operation,
            IReadOnlyCollection<KeyValuePair<string, string>>? headers,
            TRequest request,
            IRpcSerializer<TRequest> requestSerializer,
            IRpcSerializer<TResponse> responseSerializer,
            CancellationToken cancellationToken)
            where TRequest : class
        {
            if (TraceEnabled)
            {
                Logger.Trace("Begin SendReceiveFrameAsync {Operation}.", operation);
            }

            async Task<TResponse> Awaited(int messageId, ValueTask<BufferWriterStream> pendingWriteTask, Task<TResponse> response)
            {
                try
                {
                    if (TraceEnabled)
                    {
                        Logger.Trace("Awaiting writer for '{Operation}'.", operation);
                    }

                    this.WriteRequest(request, requestSerializer, await pendingWriteTask.ContextFree());

                    var awaitedResponse = await response.ContextFree();

                    if (TraceEnabled)
                    {
                        Logger.Trace("Completed SendReceiveFrameAsync '{Operation}'.", operation);
                    }

                    return awaitedResponse;
                }
                catch (Exception ex)
                {
                    this.HandleCallError(messageId, ex);
                    throw;
                }
            }

            var tcs = new ResponseCompletionSource<TResponse>(responseSerializer);
            int messageId = this.AddAwaitingResponse(tcs);
            try
            {

                RpcOperationFlags flags = 0;

                if (cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(() => this.CancelCall(messageId, operation));
                    flags |= RpcOperationFlags.CanCancel;
                }

                var frame = new LightweightRpcFrame(frameType, messageId, operation, flags, 0, headers);

                var writeTask = this.BeginWriteAsync(frame);

                if (writeTask.IsCompletedSuccessfully)
                {
                    this.WriteRequest(request, requestSerializer, writeTask.Result);

                    if (TraceEnabled)
                    {
                        tcs.Task.ContinueWith(t =>
                        {
                            switch (t.Status)
                            {
                                case TaskStatus.RanToCompletion:
                                    Logger.Trace("Completed SendReceiveFrameAsync '{Operation}'.", operation);
                                    break;
                                case TaskStatus.Canceled:
                                    Logger.Trace("SendReceiveFrameAsync cancelled '{Operation}'.", operation);
                                    break;
                                case TaskStatus.Faulted:
                                    Logger.TraceException("SendReceiveFrameAsync error '{Operation}'.", t.Exception);
                                    break;
                            }

                            return Task.CompletedTask;
                        }, TaskScheduler.Default).Forget();
                    }

                    return tcs.Task;
                }

                return Awaited(messageId, writeTask, tcs.Task);
            }
            catch (Exception ex)
            {
                this.HandleCallError(messageId, ex);
                throw;
            }
        }

#pragma warning restore CA2000 // Dispose objects before losing scope

        internal async Task<TResponse> SendReceiveFrameAsync2<TRequest, TResponse>(
            RpcFrameType frameType,
            string operation,
            IReadOnlyCollection<KeyValuePair<string, string>>? headers,
            TRequest request,
            LightweightSerializers<TRequest, TResponse> serializers,
            int timeout,
            CancellationToken cancellationToken)
            where TRequest : class
        {
            if (TraceEnabled)
            {
                Logger.Trace("Begin SendReceiveFrameAsync {Operation}.", operation);
            }

            var tcs = new ResponseCompletionSource<TResponse>(serializers.ResponseSerializer);
            int messageId = this.AddAwaitingResponse(tcs);
            try
            {
                RpcOperationFlags flags = 0;

                if (cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(() => this.CancelCall(messageId, operation));
                    flags |= RpcOperationFlags.CanCancel;
                }

                var frame = new LightweightRpcFrame(frameType, messageId, operation, flags, (uint)timeout, headers);

                var requestStream = await this.BeginWriteAsync(frame).ContextFree();
                await this.WriteRequestAsync(request, serializers.RequestSerializer, requestStream).ContextFree();

                if (timeout == 0 || RpcProxyOptions.RoundTripCancellationsAndTimeouts)
                {
                    return await tcs.Task.ContextFree();
                }
                else
                {
                    var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeout)).ContextFree();
                    if (completedTask == tcs.Task)
                    {
                        return tcs.Task.AwaiterResult();
                    }
                    else
                    {
                        throw new TimeoutException($"Operation '{operation}' didn't complete within the timeout ({timeout} ms).");
                    }
                }
            }
            catch (Exception ex)
            {
                this.HandleCallError(messageId, ex);
                throw;
            }
        }

        protected override void OnClosed(Exception? ex)
        {
            base.OnClosed(ex);

            IResponseHandler[] activeOps;
            lock (this.awaitingResponses)
            {
                activeOps = this.awaitingResponses.Values.ToArray();
                this.awaitingResponses.Clear();
            }

            foreach (var handler in activeOps)
            {
                if (ex != null)
                {
                    handler.HandleError(ex);
                }
                else
                {
                    handler.HandleCancellation();
                }
            }
        }

        protected override ValueTask OnEndReceiveLoopAsync()
        {
            this.Close();
            return default;
        }

        protected override ValueTask OnReceiveAsync(in LightweightRpcFrame frame)
        {
            int messageId = frame.MessageNumber;

            if (messageId != 0)
            {
                // request/response
                IResponseHandler? responseHandler;
                lock (this.awaitingResponses)
                {
                    this.awaitingResponses.TryGetValue(messageId, out responseHandler);
                }

                if (responseHandler != null)
                {
                    if (responseHandler.HandleResponse(frame))
                    {
                        lock (this.awaitingResponses)
                        {
                            this.awaitingResponses.Remove(messageId);
                        }

                    }
                }
            }

            if (messageId == 0)
            {
                // This is not supported
                // Log error? Close connection?
            }
            return default;
        }

        protected override Task OnReceiveLargeFrameAsync(LightweightRpcFrame frame)
        {
            int messageId = frame.MessageNumber;

            if (messageId != 0)
            {
                // request/response
                IResponseHandler? responseHandler;
                lock (this.awaitingResponses)
                {
                    this.awaitingResponses.TryGetValue(messageId, out responseHandler);
                }

                if (responseHandler != null)
                {
                    string msg = $"Size of received response frame exceeds size limit (frame size={frame.FrameLength}, max size={this.MaxReceiveFrameLength}).";
                    responseHandler.HandleError(new RpcFailureException(RpcFailure.SizeLimitExceeded, msg));
                }
            }

            return Task.CompletedTask;
        }

        protected override void OnReceiveLoopFaulted(ExceptionEventArgs e)
        {
            base.OnReceiveLoopFaulted(e);

            this.Close(e.Exception);
        }

        private int AddAwaitingResponse(IResponseHandler responseHandler)
        {
            int messageId;
            lock (this.awaitingResponses)
            {
                do
                {
                    messageId = ++this.nextMessageId;
                } while (messageId == 0 || this.awaitingResponses.ContainsKey(messageId));

                this.awaitingResponses.Add(messageId, responseHandler);
            }

            return messageId;
        }

        private async void CancelCall(int messageId, string operation)
        {
            IResponseHandler? responseHandler;
            lock (this.awaitingResponses)
            {
                if (this.awaitingResponses.TryGetValue(messageId, out responseHandler))
                {
                    if (!RpcProxyOptions.RoundTripCancellationsAndTimeouts)
                    {
                        this.awaitingResponses.Remove(messageId);
                    }
                }
            }

            if (responseHandler != null)
            {
                if (!RpcProxyOptions.RoundTripCancellationsAndTimeouts)
                {
                    responseHandler.HandleCancellation();
                }

                var frame = new LightweightRpcFrame(RpcFrameType.CancelRequest, messageId, operation, RpcOperationFlags.None, 0, null);
                var payloadStream = await this.BeginWriteAsync(frame).ContextFree();
                if (payloadStream != null)
                {
                    this.EndWriteAsync().Forget();
                }
            }
        }

        private void HandleCallError(int messageId, Exception e)
        {
            IResponseHandler? responseHandler;
            lock (this.awaitingResponses)
            {
                if (this.awaitingResponses.TryGetValue(messageId, out responseHandler))
                {
                    this.awaitingResponses.Remove(messageId);
                }
            }

            responseHandler?.HandleError(e);
        }

        private void TimeoutCall(int messageId, string operation, int timeout)
        {
            IResponseHandler? responseHandler;
            lock (this.awaitingResponses)
            {
                if (this.awaitingResponses.TryGetValue(messageId, out responseHandler))
                {
                    this.awaitingResponses.Remove(messageId);
                }
            }

            if (responseHandler != null)
            {
                responseHandler.HandleError(new TimeoutException($"Operation '{operation}' didn't complete within the timeout ({timeout} ms)."));
            }
        }

        private void WriteRequest<TRequest>(TRequest request, IRpcSerializer<TRequest> serializer, BufferWriterStream payloadStream) where TRequest : class
        {
            try
            {
                serializer.Serialize(payloadStream, request);
            }
            catch
            {
                this.AbortWrite();
                throw;
            }

            this.EndWriteAsync().Forget();
        }

        private async ValueTask WriteRequestAsync<TRequest>(TRequest request, IRpcSerializer<TRequest> serializer, BufferWriterStream payloadStream) where TRequest : class
        {
            try
            {
                serializer.Serialize(payloadStream, request);
            }
            catch
            {
                this.AbortWrite();
                throw;
            }

            await this.EndWriteAsync().ContextFree();
        }

        private class AsyncStreamingServerCall<TResponse> : IAsyncStreamingServerCall<TResponse>, IResponseHandler
            where TResponse : class
        {
            private readonly StreamingResponseStream<TResponse> responseStream;

            private IRpcSerializer<TResponse> serializer;

            /// <summary>
            /// Initializes a new ResponseCompletionSource for handling an RPC call response.
            /// Call <see cref="HandleResponse(LightweightRpcFrame)"/> to finish the RPC call.
            /// Note. Continuation must run asynchronously, otherwise there might be a dead-lock
            /// when calling synchronous methods from the continuation (since HandleResponse will be
            /// called from the receive loop).
            /// </summary>
            /// <param name="serializer"></param>
            internal AsyncStreamingServerCall(IRpcSerializer<TResponse> serializer)
            {
                this.serializer = serializer;
                this.responseStream = new StreamingResponseStream<TResponse>();
            }

            public IAsyncEnumerator<TResponse> ResponseStream => this.responseStream;

            public void Dispose()
            {
            }

            public void HandleCancellation()
            {
                this.responseStream.Cancel();
            }

            public void HandleError(Exception ex)
            {
                this.responseStream.Complete(ex);
            }

            public bool HandleResponse(LightweightRpcFrame frame)
            {
                switch (frame.FrameType)
                {
                    case RpcFrameType.StreamingResponse:
                        var response = this.serializer.Deserialize(frame.Payload);
                        this.responseStream.Enqueue(response);
                        return false;

                    case RpcFrameType.StreamingEnd:
                        // TODO Handle exceptions.
                        this.responseStream.Complete();
                        return true;
                    case RpcFrameType.CancelResponse:
                        this.responseStream.Cancel();
                        return true;
                    case RpcFrameType.TimeoutResponse:
                        this.responseStream.Complete(new TimeoutException($"Server side operation '{frame.RpcOperation}' didn't complete within the timeout."));
                        return true;
                    default:
                        // This is a pretty serious error. If it occurs, I assume there will soon
                        // be some sort of communication error.
                        throw new RpcFailureException(RpcFailure.InvalidData, $"Unexpected frame type '{frame.FrameType}' in AsyncStreamingServerCall.HandleResponse");
                }
            }
        }

        private sealed class ResponseCompletionSource<TResponse> : TaskCompletionSource<TResponse>, IResponseHandler
        {
            private IRpcSerializer<TResponse> serializer;

            /// <summary>
            /// Initializes a new ResponseCompletionSource for handling an RPC call response.
            /// Call <see cref="HandleResponse(LightweightRpcFrame)"/> to finish the RPC call.
            /// Note. Continuation must run asynchronously, otherwise there might be a dead-lock
            /// when calling synchronous methods from the continuation (since HandleResponse will be
            /// called from the receive loop).
            /// </summary>
            /// <param name="serializer"></param>
            internal ResponseCompletionSource(IRpcSerializer<TResponse> serializer)
                : base(TaskCreationOptions.RunContinuationsAsynchronously)
            {
                this.serializer = serializer;
            }

            public void HandleCancellation()
            {
                this.TrySetCanceled();
            }

            public void HandleError(Exception ex)
            {
                this.TrySetException(ex);
            }

            public bool HandleResponse(LightweightRpcFrame frame)
            {
                switch (frame.FrameType)
                {
                    case RpcFrameType.UnaryResponse:
                        var response = this.serializer.Deserialize(frame.Payload);
                        this.TrySetResult(response);
                        break;
                    case RpcFrameType.TimeoutResponse:
                        // This is not very likely, since the operation should have 
                        // already timed out on the client side.
                        this.TrySetException(new TimeoutException($"Server side operation '{frame.RpcOperation}' didn't complete within the timeout."));
                        break;
                    case RpcFrameType.CancelResponse:
                        this.TrySetCanceled();
                        break;
                    case RpcFrameType.ErrorResponse:
                        // TODO: Exception details if enabled).
                        string? message = frame.Headers?.FirstOrDefault(p => p.Key == LightweightRpcFrame.ErrorMessageHeaderKey).Value;
                        string? errorCode = frame.Headers?.FirstOrDefault(p => p.Key == LightweightRpcFrame.ErrorCodeHeaderKey).Value;
                        var failure = RpcFailureException.GetFailureFromFaultCode(errorCode);
                        this.TrySetException(new RpcFailureException(failure, message ?? $"Error occured in server handler of '{frame.RpcOperation}'"));
                        break;
                    default:
                        this.TrySetException(new RpcFailureException(RpcFailure.Unknown, $"Unexpected frame type '{frame.FrameType}' in ResponseCompletionSource.HandleResponse"));
                        // TODO: This is bad. Close connection?
                        break;
                }

                return true;
            }
        }

        private class StreamingResponseStream<TResponse> : IAsyncEnumerator<TResponse>
        {
            public Queue<TResponse> responseQueue = new Queue<TResponse>();

            private readonly object syncRoot = new object();

            private Task<bool>? completedTask;

#nullable disable   // Should only be disabled around TResponse current, but that does not seem to work currently.
            private TResponse current;

#nullable restore

            private bool hasCurrent;

            // TODO: Should probably have a CancellationToken instead?
            private bool isCancelled;

            private TaskCompletionSource<bool>? responseTcs;

            public TResponse Current
            {
                get
                {
                    lock (this.syncRoot)
                    {
                        if (!this.hasCurrent)
                        {
                            throw new InvalidOperationException();
                        }

                        return this.current;
                    }
                }
            }

            public ValueTask DisposeAsync()
            {
                // Not implemented. What should we do?
                return default;
            }

            public ValueTask<bool> MoveNextAsync()
            {
                Task<bool> nextTask;

                lock (this.syncRoot)
                {
                    if (this.responseTcs != null)
                    {
                        throw new NotSupportedException("Cannot MoveNext concurrently.");
                    }

                    if (this.isCancelled)
                    {
                        nextTask = this.completedTask!;
                    }
                    else
                    {

                        if (this.responseQueue.Count > 0)
                        {
                            this.current = this.responseQueue.Dequeue();
                            this.hasCurrent = true;
                            return new ValueTask<bool>(true);
                        }
                        else
                        {
                            this.hasCurrent = false;
                            this.current = default;
                        }

                        if (this.completedTask != null)
                        {
                            nextTask = this.completedTask;
                        }
                        else
                        {
                            this.responseTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                            nextTask = this.responseTcs.Task;
                        }
                    }
                }

                async ValueTask<bool> AwaitNext(Task<bool> t) => await t.ContextFree();

                return AwaitNext(nextTask);
            }

            internal void Cancel()
            {
                TaskCompletionSource<bool>? responseTcs = null;

                lock (this.syncRoot)
                {
                    responseTcs = this.responseTcs;
                    this.responseTcs = null;
                    this.isCancelled = true;

                    if (responseTcs != null)
                    {
                        if (this.responseQueue.Count > 0)
                        {
                            throw new InvalidOperationException("Response queue should be empty if someone is awaiting a response.");
                        }

                        this.completedTask = responseTcs.Task;
                    }
                    else
                    {
                        this.completedTask = Task.FromCanceled<bool>(new CancellationToken(true));
                    }
                }

                responseTcs?.SetCanceled();
            }


            /// <summary>
            /// Completes the response stream, with an optional error. If there's any queue responses, they will be delivered by <see cref="MoveNextAsync"/>, before
            /// it returns <c>false</c> or throws the specified error.
            /// </summary>
            /// <param name="error"></param>
            internal void Complete(Exception? error = null)
            {
                TaskCompletionSource<bool>? responseTcs = null;

                lock (this.syncRoot)
                {
                    responseTcs = this.responseTcs;
                    this.responseTcs = null;

                    if (responseTcs != null)
                    {
                        if (this.responseQueue.Count > 0)
                        {
                            throw new InvalidOperationException("Response queue should be empty if someone is awaiting a response.");
                        }

                        if (this.completedTask == null)
                        {
                            this.completedTask = responseTcs.Task;
                        }
                    }
                    else if (this.completedTask == null)
                    {
                        this.completedTask = error == null ? Task.FromResult(false) : Task.FromException<bool>(error);
                    }
                }

                if (responseTcs != null)
                {
                    if (error == null)
                    {
                        responseTcs?.SetResult(false);
                    }
                    else
                    {
                        responseTcs.SetException(error);
                    }
                }
            }


            internal void Enqueue(TResponse response)
            {
                TaskCompletionSource<bool>? responseTcs = null;
                lock (this.syncRoot)
                {
                    if (this.completedTask != null)
                    {
                        throw new InvalidOperationException("Cannot Enqueue new responses to a completed response stream.");
                    }

                    if (this.responseTcs != null)
                    {
                        // Someone is waiting for this response. Just give it to
                        // them.
                        this.current = response;
                        this.hasCurrent = true;
                        responseTcs = this.responseTcs;
                        this.responseTcs = null;
                    }
                    else
                    {
                        this.responseQueue.Enqueue(response);
                    }

                }

                responseTcs?.SetResult(true);
            }
        }
    }
}
