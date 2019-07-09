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

using SciTech.Collections;
using SciTech.IO;
using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Logging;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Client.Internal
{
    internal class RpcPipelineClient : RpcPipeline
    {
        private static readonly ILog Logger = LogProvider.For<RpcPipelineClient>();

        private readonly Dictionary<int, IResponseHandler> awaitingResponses
            = new Dictionary<int, IResponseHandler>();

        private int nextMessageId;

        private Task receiveLoopTask;

        public RpcPipelineClient(IDuplexPipe pipe, int? maxRequestSize = null, int? maxResponseSize = null) : base(pipe, maxRequestSize, maxResponseSize)
        {
            this.receiveLoopTask = this.StartReceiveLoopAsync();
        }

        private interface IResponseHandler
        {
            void HandleCancellation();

            void HandleError(Exception ex);

            bool HandleResponse(LightweightRpcFrame frame);
        }

        public Task AwaitFinished()
        {
            return this.receiveLoopTask;
        }

#pragma warning disable CA2000 // Dispose objects before losing scope
        public async ValueTask<IAsyncStreamingServerCall<TResponse>> BeginStreamingServerCall<TRequest, TResponse>(
            RpcFrameType frameType,
            string operation,
            IReadOnlyCollection<KeyValuePair<string, string>>? headers,
            TRequest request,
            IRpcSerializer serializer,
            CancellationToken cancellationToken)
            where TRequest : class
            where TResponse : class
        {
            var streamingCall = new AsyncStreamingServerCall<TResponse>(serializer);
            int messageId = this.AddAwaitingResponse(streamingCall);

            try
            {
                RpcOperationFlags flags = 0;
                if (cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(() => this.CancelCall(messageId, operation));
                    flags |= RpcOperationFlags.CanCancel;
                }

                var frame = new LightweightRpcFrame(frameType, messageId, operation, flags, 0, headers);

                var payloadStream = await this.BeginWriteAsync(frame).ContextFree();
                WriteRequest<TRequest>(request, serializer, payloadStream);

                return streamingCall;
            }
            catch (Exception e)
            {
                this.HandleCallError(messageId, e);
                throw;
            }
        }

#pragma warning restore CA2000 // Dispose objects before losing scope


#pragma warning disable CA2000 // Dispose objects before losing scope
        public Task<TResponse> SendReceiveFrameAsync<TRequest, TResponse>(
            RpcFrameType frameType,
            string operation,
            IReadOnlyCollection<KeyValuePair<string, string>>? headers,
            TRequest request,
            IRpcSerializer serializer,
            CancellationToken cancellationToken)
            where TRequest : class
        {
            Logger.Trace("Begin SendReceiveFrameAsync {Operation}.", operation);

            async Task<TResponse> Awaited(int messageId, ValueTask<Stream> pendingWriteTask, Task<TResponse> response)
            {
                try
                {
                    Logger.Trace("Awaiting writer for '{Operation}'.", operation);
                    this.WriteRequest(request, serializer, await pendingWriteTask.ContextFree());

                    var awaitedResponse = await response.ContextFree();
                    Logger.Trace("Completed SendReceiveFrameAsync '{Operation}'.", operation);
                    return awaitedResponse;
                }
                catch (Exception ex)
                {
                    this.HandleCallError(messageId, ex);
                    throw;
                }
            }

            var tcs = new ResponseCompletionSource<TResponse>(serializer);
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
                    this.WriteRequest(request, serializer, writeTask.Result);

                    if (Logger.IsTraceEnabled())
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

        public async Task<TResponse> SendReceiveFrameAsync2<TRequest, TResponse>(
            RpcFrameType frameType,
            string operation,
            IReadOnlyCollection<KeyValuePair<string, string>>? headers,
            TRequest request,
            IRpcSerializer serializer,
            CancellationToken cancellationToken)
            where TRequest : class
        {
            Logger.Trace("Begin SendReceiveFrameAsync {Operation}.", operation);

            var tcs = new ResponseCompletionSource<TResponse>(serializer);
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

                var requestStream = await this.BeginWriteAsync(frame).ContextFree();
                await this.WriteRequestAsync(request, serializer, requestStream).ContextFree();

                return await tcs.Task.ContextFree();
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

        protected override void OnReceiveLoopFaulted(Exception e)
        {
            this.Close(e);
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
            bool canCancel;
            lock (this.awaitingResponses)
            {
                canCancel = this.awaitingResponses.ContainsKey(messageId);
            }

            // TODO: Doesn't it make sense to cancel the response immediately. Is
            // there a need to round-trip the cancellation to the server? 
            if (canCancel)
            {
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

        private void WriteRequest<TRequest>(TRequest request, IRpcSerializer serializer, Stream payloadStream) where TRequest : class
        {
            try
            {
                serializer.ToStream(payloadStream, request);
            }
            catch
            {
                this.AbortWrite();
                throw;
            }

            this.EndWriteAsync().Forget();
        }

        private async ValueTask WriteRequestAsync<TRequest>(TRequest request, IRpcSerializer serializer, Stream payloadStream) where TRequest : class
        {
            try
            {
                serializer.ToStream(payloadStream, request);
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

            private IRpcSerializer serializer;

            /// <summary>
            /// Initializes a new ResponseCompletionSource for handling an RPC call response.
            /// Call <see cref="HandleResponse(LightweightRpcFrame)"/> to finish the RPC call.
            /// Note. Continuation must run asynchronously, otherwise there might be a dead-lock
            /// when calling synchronous methods from the continuation (since HandleResponse will be
            /// called from the receive loop).
            /// </summary>
            /// <param name="serializer"></param>
            internal AsyncStreamingServerCall(IRpcSerializer serializer)
            {
                this.serializer = serializer;
                this.responseStream = new StreamingResponseStream<TResponse>();
            }

            public IAsyncStream<TResponse> ResponseStream => this.responseStream;

            public void Dispose()
            {
            }

            public void HandleCancellation()
            {
                this.responseStream.Complete();
            }

            public void HandleError(Exception ex)
            {
                this.responseStream.Complete();
            }

            public bool HandleResponse(LightweightRpcFrame frame)
            {
                switch (frame.FrameType)
                {
                    case RpcFrameType.StreamingResponse:
                        using (var responsePayloadStream = frame.Payload.AsStream())
                        {
                            var response = (TResponse)this.serializer.FromStream(typeof(TResponse), responsePayloadStream);

                            this.responseStream.Enqueue(response);
                        }
                        return false;

                    case RpcFrameType.StreamingEnd:
                        // TODO Handle exceptions.
                        this.responseStream.Complete();
                        return true;
                    default:
                        throw new RpcFailureException($"Unexpected frame type '{frame.FrameType}' in AsyncStreamingServerCall.HandleResponse");
                }
            }
        }

        private sealed class ResponseCompletionSource<TResponse> : TaskCompletionSource<TResponse>, IResponseHandler
        {
            private IRpcSerializer serializer;

            /// <summary>
            /// Initializes a new ResponseCompletionSource for handling an RPC call response.
            /// Call <see cref="HandleResponse(LightweightRpcFrame)"/> to finish the RPC call.
            /// Note. Continuation must run asynchronously, otherwise there might be a dead-lock
            /// when calling synchronous methods from the continuation (since HandleResponse will be
            /// called from the receive loop).
            /// </summary>
            /// <param name="serializer"></param>
            internal ResponseCompletionSource(IRpcSerializer serializer)
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
                        using (var responseStream = frame.Payload.AsStream())
                        {
                            var response = (TResponse)this.serializer.FromStream(typeof(TResponse), responseStream);

                            this.TrySetResult(response);
                        }
                        break;
                    case RpcFrameType.CancelResponse:
                        this.TrySetCanceled();
                        break;
                    case RpcFrameType.ErrorResponse:
                        // TODO: Error message (exception details if enabled).
                        this.TrySetException(new RpcFailureException($"Error occured in server handler of '{frame.RpcOperation}'"));
                        break;
                    default:
                        this.TrySetException(new RpcFailureException($"Unexpected frame type '{frame.FrameType}' in ResponseCompletionSource.HandleResponse"));
                        // TODO: This is bad. Close connection
                        break;
                }

                return true;
            }
        }

#nullable disable   // Should only be disabled around TResponse current, but that does not seem to work currently.
        private class StreamingResponseStream<TResponse> : IAsyncStream<TResponse>
        {
            public Queue<TResponse> responseQueue = new Queue<TResponse>();

            private readonly object syncRoot = new object();

            private TResponse current;

            private bool hasCurrent;

            private bool isEnded;

            private TaskCompletionSource<bool> responseTcs;

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

            public void Dispose()
            {
                // Not implemented. What should we do?
            }

            public ValueTask<bool> MoveNextAsync()
            {
                //TaskCompletionSource<bool> responseTcs;
                Task<bool> nextTask;

                lock (this.syncRoot)
                {
                    if (this.responseTcs != null)
                    {
                        throw new NotSupportedException("Cannot MoveNext concurrently.");
                    }

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

                    if (this.isEnded)
                    {
                        // TODO: Handles exceptions.
                        return new ValueTask<bool>(false);
                    }

                    this.responseTcs = new TaskCompletionSource<bool>();
                    nextTask = this.responseTcs.Task;
                }

                async ValueTask<bool> AwaitNext() => await nextTask.ContextFree();

                return AwaitNext();
            }

            internal void Complete()
            {
                TaskCompletionSource<bool> responseTcs = null;

                lock (this.syncRoot)
                {
                    this.isEnded = true;
                    responseTcs = this.responseTcs;
                    this.responseTcs = null;

                    if (responseTcs != null && this.responseQueue.Count > 0)
                    {
                        throw new InvalidOperationException("Response queue should be empty if someone is awaiting a response.");
                    }
                }

                responseTcs?.SetResult(false);
            }

            internal void Enqueue(TResponse response)
            {
                TaskCompletionSource<bool> responseTcs = null;
                lock (this.syncRoot)
                {
                    if (this.isEnded)
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
#nullable restore
    }
}
