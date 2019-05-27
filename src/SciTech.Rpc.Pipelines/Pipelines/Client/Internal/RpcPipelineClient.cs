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
using SciTech.Rpc.Pipelines.Internal;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Pipelines.Client.Internal
{
    internal class RpcPipelineClient : RpcPipeline
    {
        private readonly Dictionary<int, IResponseHandler> awaitingResponses
            = new Dictionary<int, IResponseHandler>();

        private int nextMessageId;

        private Task receiveLoopTask;

        public RpcPipelineClient(IDuplexPipe pipe) : base(pipe)
        {
            this.receiveLoopTask = this.StartReceiveLoopAsync();
        }

        private interface IResponseHandler
        {
            bool IsStreaming { get; }

            void HandleResponse(RpcPipelinesFrame frame);
        }

        public Task AwaitFinished()
        {
            return this.receiveLoopTask;
        }

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

            RpcOperationFlags flags = 0;
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() => this.CancelCall(messageId, operation));
                flags |= RpcOperationFlags.CanCancel;
            }


            var frame = new RpcPipelinesFrame(frameType, messageId, operation, flags, 0, headers);

            var payloadStream = await this.BeginWriteAsync(frame).ContextFree();
            using (payloadStream)
            {
                serializer.ToStream(payloadStream, request);
            }

            this.EndWriteAsync().Forget();
            return streamingCall;
        }

        public Task<TResponse> SendReceiveFrameAsync<TRequest, TResponse>(
            RpcFrameType frameType,
            string operation,
            IReadOnlyCollection<KeyValuePair<string, string>>? headers,
            TRequest request,
            IRpcSerializer serializer,
            CancellationToken cancellationToken)
            where TRequest : class
        {
            async Task<TResponse> Awaited(ValueTask<Stream> pendingWriteTask, Task<TResponse> response)
            {
                using (var payloadStream = await pendingWriteTask.ContextFree())
                {
                    serializer.ToStream(payloadStream, request);
                }

                this.EndWriteAsync().Forget();

                return await response.ContextFree();
            }

            var tcs = new ResponseCompletionSource<TResponse>(serializer);
            int messageId = this.AddAwaitingResponse(tcs);

            RpcOperationFlags flags = 0;

            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() => this.CancelCall(messageId, operation));
                flags |= RpcOperationFlags.CanCancel;
            }


            var frame = new RpcPipelinesFrame(frameType, messageId, operation, flags, 0, headers);

            var writeTask = this.BeginWriteAsync(frame);

            if (writeTask.IsCompletedSuccessfully)
            {
                using (var payloadStream = writeTask.Result)
                {
                    serializer.ToStream(payloadStream, request);
                }

                this.EndWriteAsync().Forget();

                return tcs.Task;
            }


            return Awaited(writeTask, tcs.Task);
        }
        //public Task<RpcPipelinesFrame> SendReceiveFrameAsync(
        //    RpcFrameType frameType, string operation, ImmutableArray<KeyValuePair<string, string>> headers,
        //    Action<Stream> payloadWriter)
        //{
        //    async Task<RpcPipelinesFrame> Awaited(ValueTask<Stream> pendingWriteTask, Action<Stream> writer, Task<RpcPipelinesFrame> response)
        //    {
        //        using (var payloadStream = await pendingWriteTask.ContextFree())
        //        {
        //            writer(payloadStream);
        //        }
        //        return await response.ContextFree();
        //    }
        //    var tcs = new TaskCompletionSource<RpcPipelinesFrame>();
        //    int messageId;
        //    lock (this.awaitingResponses)
        //    {
        //        do
        //        {
        //            messageId = ++this._nextMessageId;
        //        } while (messageId == 0 || this.awaitingResponses.ContainsKey(messageId));
        //        this.awaitingResponses.Add(messageId, tcs);
        //    }
        //    var frame = new RpcPipelinesFrame(frameType, messageId, operation, headers);
        //    var writeTask = this.BeginWriteAsync(frame);
        //    if (writeTask.IsCompletedSuccessfully)
        //    {
        //        using (var payloadStream = writeTask.Result)
        //        {
        //            payloadWriter(payloadStream);
        //        }
        //        return tcs.Task;
        //    }
        //    return Awaited(writeTask, payloadWriter, tcs.Task);
        //}

        protected override ValueTask OnReceiveAsync(in RpcPipelinesFrame frame)
        {
            int messageId = frame.MessageNumber;

            if (messageId != 0)
            {
                // request/response
                IResponseHandler? responseHandler;
                lock (this.awaitingResponses)
                {
                    if (this.awaitingResponses.TryGetValue(messageId, out responseHandler))
                    {
                        if (!responseHandler.IsStreaming)
                        {
                            this.awaitingResponses.Remove(messageId);
                        }
                    }
                    else
                    {   // didn't find a twin, but... meh
                        responseHandler = null;
                        messageId = 0; // treat as MessageReceived
                    }
                }
                if (responseHandler != null)
                {
                    responseHandler.HandleResponse(frame);
                }
            }

            if (messageId == 0)
            {
                // This is not supported
                // Log error? Close connection?
            }
            return default;
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

            if (canCancel)
            {
                var frame = new RpcPipelinesFrame(RpcFrameType.CancelRequest, messageId, operation, RpcOperationFlags.None, 0, null);
                var payloadStream = await this.BeginWriteAsync(frame).ContextFree();
                using (payloadStream) { }
                this.EndWriteAsync().Forget();
            }
        }

        private class AsyncStreamingServerCall<TResponse> : IAsyncStreamingServerCall<TResponse>, IResponseHandler
            where TResponse : class
        {
            private readonly StreamingResponseStream<TResponse> responseStream;

            private IRpcSerializer serializer;

            /// <summary>
            /// Initializes a new ResponseCompletionSource for handling an RPC call response.
            /// Call <see cref="HandleResponse(RpcPipelinesFrame)"/> to finish the RPC call.
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

            public bool IsStreaming => true;

            public IAsyncStream<TResponse> ResponseStream => this.responseStream;

            public void Dispose()
            {
            }

            public void HandleResponse(RpcPipelinesFrame frame)
            {
                switch (frame.FrameType)
                {
                    case RpcFrameType.StreamingResponse:
                        using (var responsePayloadStream = frame.Payload.AsStream())
                        {
                            var response = (TResponse)this.serializer.FromStream(typeof(TResponse), responsePayloadStream);

                            this.responseStream.Enqueue(response);
                        }
                        break;
                    case RpcFrameType.StreamingEnd:
                        // TODO Handle exceptions.
                        this.responseStream.Complete();
                        break;
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
            /// Call <see cref="HandleResponse(RpcPipelinesFrame)"/> to finish the RPC call.
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

            public bool IsStreaming => true;

            public void HandleResponse(RpcPipelinesFrame frame)
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
                        this.TrySetException(new RpcFailureException($"Error occured in server handler of '{frame.RpcOperation}'") );
                        break;
                    default:
                        this.TrySetException(new RpcFailureException($"Unexpected frame type '{frame.FrameType}' in ResponseCompletionSource.HandleResponse"));
                        // TODO: This is bad. Close connection
                        break;
                }
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
