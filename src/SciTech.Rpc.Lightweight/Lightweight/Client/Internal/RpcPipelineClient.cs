#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB
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

using SciTech.Rpc.Client;
using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Logging;
using SciTech.Rpc.Serialization;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Client.Internal
{
    internal class RpcPipelineClient : RpcPipeline
    {
        [SuppressMessage("Performance", "CA1802:Use literals where appropriate", Justification = "Logging temporarily removed")]
        private static readonly bool TraceEnabled = false; //  Logger.IsTraceEnabled();

        private readonly Dictionary<int, IResponseHandler> awaitingResponses
            = new Dictionary<int, IResponseHandler>();

        private int nextMessageId;

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



#pragma warning disable CA2000 // Dispose objects before losing scope
        public IAsyncStreamingServerCall<TResponse> BeginStreamingServerCall<TRequest, TResponse>(
            RpcFrameType frameType,
            string operation,
            RpcRequestContext? context,
            TRequest request,
            LightweightSerializers<TRequest, TResponse> serializers,
            int callTimeout)
            where TRequest : class
            where TResponse : class
        {
            var streamingCall = new AsyncStreamingServerCall<TResponse>(serializers.Serializer, serializers.ResponseSerializer);
            int messageId = this.AddAwaitingResponse(streamingCall);

            try
            {
                RpcOperationFlags flags = 0;
                var cancellationToken = context != null ? context.CancellationToken : default;
                if (cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(() => this.CancelCall(messageId, operation));
                    flags |= RpcOperationFlags.CanCancel;
                }

                if (callTimeout > 0 && !RpcProxyOptions.RoundTripCancellationsAndTimeouts)
                {
                    Task.Delay(callTimeout)
                        .ContinueWith(t => 
                            this.TimeoutCall(messageId, operation, callTimeout), cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)
                        .Forget();
                }

                var frame = new LightweightRpcFrame(frameType, messageId, operation, flags, (uint)callTimeout, context?.Headers);

                var writeState = this.BeginWrite(frame);
                this.WriteRequest(request, serializers.RequestSerializer, writeState);

                return streamingCall;
            }
            catch (Exception e)
            {
                this.HandleCallError(messageId, e);
                streamingCall.DisposeAsync().Forget();
                throw;
            }
        }

#pragma warning restore CA2000 // Dispose objects before losing scope


        public async Task RunAsyncCore()
        {
            try
            {
                await this.StartReceiveLoopAsync().ContextFree();
            }
            finally
            {
                await this.CloseAsync().ContextFree();
            }
        }


        internal Task<TResponse> SendReceiveFrameAsync<TRequest, TResponse>(
            RpcFrameType frameType,
            string operation,
            RpcRequestContext? context,
            TRequest request,
            LightweightSerializers<TRequest, TResponse> serializers,
            int timeout)
            where TRequest : class
        {
            if (TraceEnabled)
            {
                // TODO: Logger.Trace("Begin SendReceiveFrameAsync {Operation}.", operation);
            }

            var tcs = new ResponseCompletionSource<TResponse>(serializers.Serializer, serializers.ResponseSerializer);

            int messageId = this.AddAwaitingResponse(tcs);
            try
            {
                RpcOperationFlags flags = 0;

                var cancellationToken = context != null ? context.CancellationToken : default;
                if (cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(() => this.CancelCall(messageId, operation));
                    flags |= RpcOperationFlags.CanCancel;
                }

                var frame = new LightweightRpcFrame(frameType, messageId, operation, flags, (uint)timeout, context?.Headers);

                var writeState = this.BeginWrite(frame);

                var writeTask = this.WriteRequestAsync(request, serializers.RequestSerializer, writeState );
                if (writeTask.IsCompletedSuccessfully)
                {
                    if (timeout == 0 || RpcProxyOptions.RoundTripCancellationsAndTimeouts)
                    {
                        return tcs.Task;
                    }
                    else
                    {
                        return AwaitTimeoutResponse(tcs.Task, operation, timeout);
                    }
                }
                else
                {
                    return AwaitWrite(writeTask, tcs.Task);
                }
            }
            catch (Exception ex)
            {
                this.HandleCallError(messageId, ex);
                throw;
            }

            async Task<TResponse> AwaitTimeoutResponse(Task<TResponse> responseTask, string operation, int timeout)
            {
                var completedTask = await Task.WhenAny(responseTask, Task.Delay(timeout)).ContextFree();
                if (completedTask == responseTask)
                {
                    return responseTask.AwaiterResult();
                }
                else
                {
                    throw new TimeoutException($"Operation '{operation}' didn't complete within the timeout ({timeout} ms).");
                }
            }


            async Task<TResponse> AwaitWrite(ValueTask writeTask, Task<TResponse> responseTask)
            {
                await writeTask.ContextFree();

                if (timeout == 0 || RpcProxyOptions.RoundTripCancellationsAndTimeouts)
                {
                    return await responseTask.ContextFree();
                }
                else
                {
                    return await AwaitTimeoutResponse(responseTask, operation, timeout).ContextFree();
                }
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
            return new ValueTask(this.CloseAsync());
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

        protected override async Task OnReceiveLoopFaultedAsync(ExceptionEventArgs e)
        {
            // TODO: Logger.Warn(ex, "RpcPipeline receive loop ended with error '{Error}'", ex.Message);
            
            await base.OnReceiveLoopFaultedAsync(e).ContextFree();
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

        private void CancelCall(int messageId, string operation)
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
                var writeState = this.BeginWrite(frame);
                this.EndWriteAsync(writeState, false).Forget();
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

        private void WriteRequest<TRequest>(TRequest request, IRpcSerializer<TRequest> serializer, in LightweightRpcFrame.WriteState writeState ) where TRequest : class
        {
            try
            {
                serializer.Serialize(writeState.Writer, request);
            }
            catch
            {
                this.AbortWrite(writeState);
                throw;
            }

            this.EndWriteAsync(writeState, false).Forget();
        }

        private ValueTask WriteRequestAsync<TRequest>(TRequest request, IRpcSerializer<TRequest> serializer, in LightweightRpcFrame.WriteState writeState) where TRequest : class
        {
            try
            {
                serializer.Serialize(writeState.Writer, request);
            }
            catch
            {
                this.AbortWrite(writeState);
                throw;
            }

            return this.EndWriteAsync(writeState,false);
        }

        private static RpcError? ExtractRpcError(in LightweightRpcFrame frame, IRpcSerializer serializer)
        {
            // TODO: Exception details if enabled).

            var errorInfoData = frame.GetHeaderBytes(WellKnownHeaderKeys.ErrorInfo);
            if( !errorInfoData.IsDefaultOrEmpty)
            {
                return serializer.Deserialize<RpcError>(errorInfoData.ToArray());
            }

            string? message = frame.GetHeaderString(WellKnownHeaderKeys.ErrorMessage);
            string? errorType = frame.GetHeaderString(WellKnownHeaderKeys.ErrorType);
            string? errorCode = frame.GetHeaderString(WellKnownHeaderKeys.ErrorCode);
            ImmutableArray<byte> faultDetails = frame.GetHeaderBytes(WellKnownHeaderKeys.ErrorDetails);

            if (!string.IsNullOrEmpty(errorType))
            {
                return new RpcError { ErrorType = errorType, Message = message, ErrorCode = errorCode, ErrorDetails = !faultDetails.IsDefaultOrEmpty ? faultDetails.ToArray() : null };
            }

            return null;
        }


        private class AsyncStreamingServerCall<TResponse> : IAsyncStreamingServerCall<TResponse>, IResponseHandler
        {
            private readonly StreamingResponseStream<TResponse> responseStream;

            private IRpcSerializer serializer;
            private IRpcSerializer<TResponse> responseSerializer;

            /// <summary>
            /// Initializes a new ResponseCompletionSource for handling an RPC call response.
            /// Call <see cref="HandleResponse(LightweightRpcFrame)"/> to finish the RPC call.
            /// Note. Continuation must run asynchronously, otherwise there might be a dead-lock
            /// when calling synchronous methods from the continuation (since HandleResponse will be
            /// called from the receive loop).
            /// </summary>
            /// <param name="serializer"></param>
            internal AsyncStreamingServerCall(IRpcSerializer serializer, IRpcSerializer<TResponse> responseSerializer)
            {
                this.serializer = serializer;
                this.responseSerializer = responseSerializer;
                this.responseStream = new StreamingResponseStream<TResponse>();
            }

            public IAsyncEnumerator<TResponse> ResponseStream => this.responseStream;

            public ValueTask DisposeAsync()
            {
                return this.responseStream.DisposeAsync();
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
                        var response = this.responseSerializer.Deserialize(frame.Payload);
                        this.responseStream.Enqueue(response);
                        return false;
                    case RpcFrameType.ErrorResponse:
                        {
                            var error = ExtractRpcError(frame, this.serializer);
                            if (error != null)
                            {
                                responseStream.Complete(new RpcErrorException(error));
                            } else
                            {
                                responseStream.Complete(
                                    new RpcFailureException(RpcFailure.Unknown, $"Error information not provided for '{frame.RpcOperation}'."));
                            }

                            return true;
                        }
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
            private IRpcSerializer serializer;
            private IRpcSerializer<TResponse> responseSerializer;

            /// <summary>
            /// Initializes a new ResponseCompletionSource for handling an RPC call response.
            /// Call <see cref="HandleResponse(LightweightRpcFrame)"/> to finish the RPC call.
            /// Note. Continuation must run asynchronously, otherwise there might be a dead-lock
            /// when calling synchronous methods from the continuation (since HandleResponse will be
            /// called from the receive loop).
            /// </summary>
            /// <param name="serializer"></param>
            internal ResponseCompletionSource(IRpcSerializer serializer, IRpcSerializer<TResponse> responseSerializer)
                : base(TaskCreationOptions.RunContinuationsAsynchronously)
            {
                this.serializer = serializer;
                this.responseSerializer = responseSerializer;
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
                        var response = this.responseSerializer.Deserialize(frame.Payload);
                        this.TrySetResult(response!);
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
                        {
                            RpcError? error = ExtractRpcError(frame, this.serializer);
                            if (error != null)
                            {
                                this.TrySetException(new RpcErrorException(error));
                            } else
                            {
                                this.TrySetException(new RpcFailureException(RpcFailure.Unknown, $"Error information not provided for '{frame.RpcOperation}'."));
                            }
                            return true;
                        }
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


            internal void Enqueue([AllowNull]TResponse response)
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
                        this.responseQueue.Enqueue(response!);
                    }

                }

                responseTcs?.SetResult(true);
            }
        }
    }
}
