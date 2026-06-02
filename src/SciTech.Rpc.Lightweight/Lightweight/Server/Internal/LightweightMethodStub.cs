#region Copyright notice and license

// Copyright (c) 2019-2021, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License.
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//

#endregion

using SciTech.Rpc.Client;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server.Internal
{
    internal class LightweightCallContext : IRpcServerContext, IRpcServerContextBuilder
    {
        private readonly IReadOnlyCollection<KeyValuePair<string, ImmutableArray<byte>>>? headers;

        private IPrincipal? user;

        public LightweightCallContext(
            LightweightRpcEndPoint endPoint,
            EstablishedConnection? establishedConnection,
            IReadOnlyCollection<KeyValuePair<string, ImmutableArray<byte>>>? headers,
            CancellationToken cancellationToken)
        {
            this.EndPoint = endPoint;
            this.EstablishedConnection = establishedConnection;
            this.User = user;
            this.CancellationToken = cancellationToken;
            this.headers = headers;
        }

        public CancellationToken CancellationToken { get; }

        public LightweightRpcEndPoint EndPoint { get; private set; }

        public EstablishedConnection? EstablishedConnection { get; }

        public IPrincipal? User
        {
            get => this.user ?? this.EstablishedConnection?.User;
            set => this.user = value;
        }

        public ImmutableArray<byte> GetBinaryHeader(string key)
        {
            if (this.headers != null && this.headers.Count > 0)
            {
                foreach (var pair in this.headers)
                {
                    if (pair.Key == key)
                    {
                        return pair.Value;
                    }
                }
            }
            return default;
        }

        public string? GetHeaderString(string key)
        {
            if (this.headers != null && this.headers.Count > 0)
            {
                foreach (var pair in this.headers)
                {
                    if (pair.Key == key)
                    {
                        return RpcRequestContext.StringFromHeaderBytes(pair.Value);
                    }
                }
            }

            return null;
        }
    }

    internal abstract class LightweightMethodStub : RpcMethodStub
    {
        public LightweightMethodStub(string operationName, IRpcSerializer serializer, RpcServerFaultHandler? faultHandler)
            : base(serializer, faultHandler)
        {
            this.OperationName = operationName;
        }

        public string OperationName { get; }

        public abstract Type RequestType { get; }

        public abstract Type ResponseType { get; }

        /// <summary>
        /// Gets a value indicating whether this stub handles a client-streaming operation, i.e.
        /// the client uploads a stream of items using <see cref="RpcFrameType.StreamingData"/> frames.
        /// </summary>
        public virtual bool IsClientStreaming => false;

        public abstract Task HandleMessage(ILightweightRpcFrameWriter pipeline, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context);

        public abstract Task HandleStreamingMessage(ILightweightRpcFrameWriter pipeline, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context);

        /// <summary>
        /// Begins handling a client-streaming request. Only valid when <see cref="IsClientStreaming"/> is <c>true</c>.
        /// The returned feed is used by the pipeline to push subsequent <see cref="RpcFrameType.StreamingData"/> and
        /// <see cref="RpcFrameType.StreamingEnd"/> frames to the running handler.
        /// </summary>
        public virtual IClientStreamFeed BeginClientStreaming(ILightweightRpcFrameWriter pipeline, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context)
        {
            throw new NotSupportedException("This method stub does not support client-streaming.");
        }
    }

    /// <summary>
    /// Feed used by the server pipeline to push uploaded stream data to a running client-streaming handler.
    /// </summary>
    internal interface IClientStreamFeed : IDisposable
    {
        /// <summary>
        /// Deserializes a stream item from the supplied <see cref="RpcFrameType.StreamingData"/> frame and writes
        /// it to the request stream.
        /// </summary>
        ValueTask WriteDataAsync(in LightweightRpcFrame frame);

        /// <summary>
        /// Marks the request stream as completed, in response to a <see cref="RpcFrameType.StreamingEnd"/> frame.
        /// </summary>
        void CompleteData();

        /// <summary>
        /// Faults the request stream, e.g. if the connection is lost before the stream is completed.
        /// </summary>
        void Fault(Exception exception);
        Task RunAsync();
    }


    internal class LightweightMethodStub<TRequest, TResponse> : LightweightMethodStub
        where TRequest : class, IObjectRequest
        where TResponse : class
    {
        private readonly IRpcSerializer<TRequest> requestSerializer;

        private readonly IRpcSerializer<TResponse> responseSerializer;

        ///// <summary>
        ///// Creates a method stub that will handle streaming requests.
        ///// </summary>
        ///// <param name="fullName">Full name of unary operation.</param>
        ///// <param name="streamHandler">Delegate that will be invoked to handle the request.</param>
        //public LightweightMethodStub(
        //    string fullName,
        //    Func<TRequest, IServiceProvider?, IRpcAsyncStreamWriter<TResponse>, LightweightCallContext, ValueTask> streamHandler,
        //    IRpcSerializer serializer, RpcServerFaultHandler? faultHandler)
        //    : base(fullName, serializer, faultHandler)
        //{
        //    this.StreamHandler = streamHandler;
        //    this.requestSerializer = this.Serializer.CreateTyped<TRequest>();
        //    this.responseSerializer = this.Serializer.CreateTyped<TResponse>();
        //}

        /// <summary>
        /// Creates a method stub that will handle unary requests.
        /// </summary>
        /// <param name="fullName">Full name of unary operation.</param>
        /// <param name="handler">Delegate that will be invoked to handle the request.</param>
        public LightweightMethodStub(
            string fullName,
            Func<TRequest, IServiceProvider?, LightweightCallContext, ValueTask<TResponse>> handler,
            IRpcSerializer serializer, RpcServerFaultHandler? faultHandler,
            bool allowInlineExecution)
            : base(fullName, serializer, faultHandler)
        {
            this.Handler = handler;
            this.AllowInlineExecution = allowInlineExecution;

            this.requestSerializer = this.Serializer.CreateTyped<TRequest>();
            this.responseSerializer = this.Serializer.CreateTyped<TResponse>();
        }

        public override Type RequestType => typeof(TRequest);

        public override Type ResponseType => typeof(TResponse);

        internal bool AllowInlineExecution { get; }

        /// <summary>
        /// Gets the handler for a unary request.
        /// </summary>
        internal Func<TRequest, IServiceProvider?, LightweightCallContext, ValueTask<TResponse>> Handler { get; }

        public override Task HandleMessage(ILightweightRpcFrameWriter frameWriter, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context)
        {
            TRequest? request;
            try
            {
                request = this.requestSerializer.Deserialize(frame.Payload, null);
            }
            catch (Exception ex) 
            {
                throw new RpcFailureException(RpcFailure.InvalidData, $"Payload deserialization failed. {ex.Message}", ex);
            }

            if (request == null) throw new RpcFailureException(RpcFailure.InvalidData, "Payload deserialization failed");

            if (this.AllowInlineExecution)
            {
                // In case the PipeScheduler is set to Inline, this is a bit (more) dangerous, i.e. even
                // higher risk of dead-lock. AllowInlineExecution and PipeScheduler.Inline should
                // be used with caution.
                return ExecuteOperation(this, frameWriter, request, frame.MessageNumber, frame.RpcOperation, serviceProvider, context);
            }
            else
            {
                return StartHandleMessage(frameWriter, frame, serviceProvider, context, request);
            }

            static Task ExecuteOperation(
                LightweightMethodStub<TRequest, TResponse> stub,
                ILightweightRpcFrameWriter frameWriter, TRequest request, int messageNumber, string operation,
                IServiceProvider? serviceProvider, LightweightCallContext context)
            {
                var responseTask = stub.Handler(request, serviceProvider, context);

                return stub.HandleResponse(messageNumber, operation, frameWriter, responseTask);
            }

            Task StartHandleMessage(ILightweightRpcFrameWriter frameWriter, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context, TRequest request)
            {
                var messageNumber = frame.MessageNumber;
                var operation = frame.RpcOperation;
                // TODO: Scheduler should be a stub property
                var scheduler = TaskScheduler.Default;
                //ThreadPool.UnsafeQueueUserWorkItem(
                //    oStub => ExecuteOperation((LightweightMethodStub<TRequest, TResponse>)oStub, pipeline, request, messageNumber, operation, serviceProvider, context),
                //    this);
                //)
                return Task.Factory.StartNew(
                    oStub => ExecuteOperation((LightweightMethodStub<TRequest, TResponse>)oStub!, frameWriter, request, messageNumber, operation, serviceProvider, context),
                    this,
                    context.CancellationToken,
                    TaskCreationOptions.DenyChildAttach, scheduler).Unwrap();
            }
        }

        public override Task HandleStreamingMessage(ILightweightRpcFrameWriter pipeline, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context)
        {
            throw new RpcFailureException(RpcFailure.RemoteDefinitionError, $"Server streaming request handler is not initialized for '{frame.RpcOperation}'.");
        }

        private Task HandleResponse(int messageNumber, string operation, ILightweightRpcFrameWriter frameWriter, ValueTask<TResponse> responseTask)
        {
            // Try to return response from synchronous methods directly.
            if (responseTask.IsCompletedSuccessfully)
            {
                var response = responseTask.Result;
                var headers = ImmutableArray<KeyValuePair<string, ImmutableArray<byte>>>.Empty;  // TODO:
                var responseHeader = new LightweightRpcFrame(RpcFrameType.UnaryResponse, messageNumber, operation, headers);

                var writeState = frameWriter.BeginWrite(responseHeader);
                BufferWriterStream responseStream = writeState.Writer;

                try
                {
                    this.responseSerializer.Serialize(responseStream, response);
                }
                catch
                {
                    frameWriter.AbortWrite(writeState);
                    throw;
                }

                var endWriteTask = frameWriter.EndWriteAsync(writeState, false);
                if (!endWriteTask.IsCompletedSuccessfully)
                {
                    return endWriteTask.AsTask();
                }

                // Perfect, everything is synchronous.
                return Task.CompletedTask;
            }
            else
            {
                // Handler is asynchronous (or an error occurred)
                async Task AwaitAndWriteResponse(int messageNumber, string operation)
                {
                    var response = await responseTask.ContextFree();
                    var headers = ImmutableArray<KeyValuePair<string, ImmutableArray<byte>>>.Empty;  // TODO:
                    var responseHeader = new LightweightRpcFrame(RpcFrameType.UnaryResponse, messageNumber, operation, headers);

                    var writeState = frameWriter.BeginWrite(responseHeader);
                    BufferWriterStream responseStream = writeState.Writer;
                    try
                    {
                        this.responseSerializer.Serialize(responseStream, response);
                    }
                    catch
                    {
                        frameWriter.AbortWrite(writeState);
                        throw;
                    }

                    await frameWriter.EndWriteAsync(writeState, false).ContextFree();
                }

                return AwaitAndWriteResponse(messageNumber, operation);
            }
        }
    }

    internal class LightweightStreamingMethodStub<TRequest, TStreamResponse> : LightweightMethodStub
            where TRequest : class, IObjectRequest
    {
        private readonly IRpcSerializer<TRequest> requestSerializer;

        private readonly IRpcSerializer<TStreamResponse> responseSerializer;

        /// <summary>
        /// Creates a method stub that will handle streaming requests.
        /// </summary>
        /// <param name="fullName">Full name of unary operation.</param>
        /// <param name="streamHandler">Delegate that will be invoked to handle the request.</param>
        public LightweightStreamingMethodStub(
            string fullName,
            Func<TRequest, IServiceProvider?, IRpcAsyncStreamWriter<TStreamResponse>, LightweightCallContext, ValueTask> streamHandler,
            IRpcSerializer serializer, RpcServerFaultHandler? faultHandler)
            : base(fullName, serializer, faultHandler)
        {
            this.StreamHandler = streamHandler;
            this.requestSerializer = this.Serializer.CreateTyped<TRequest>();
            this.responseSerializer = this.Serializer.CreateTyped<TStreamResponse>();
        }

        public override Type RequestType => typeof(TRequest);

        public override Type ResponseType => typeof(RpcResponse);

        /// <summary>
        /// Gets the handler for a streaming request.
        /// </summary>
        internal Func<TRequest, IServiceProvider?, IRpcAsyncStreamWriter<TStreamResponse>, LightweightCallContext, ValueTask> StreamHandler { get; }

        public override Task HandleMessage(ILightweightRpcFrameWriter pipeline, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context)
        {
            throw new NotSupportedException();
        }

        public override Task HandleStreamingMessage(ILightweightRpcFrameWriter pipeline, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context)
        {
            TRequest? request;
            try
            {
                request = this.requestSerializer.Deserialize(frame.Payload, null);
            }
            catch (Exception ex)
            {
                throw new RpcFailureException(RpcFailure.InvalidData, $"Payload deserialization failed. {ex.Message}", ex);
            }

            if (request == null) throw new RpcFailureException(RpcFailure.InvalidData, "Payload deserialization failed");

            var responseWriter = new StreamingResponseWriter<TStreamResponse>(pipeline, this.responseSerializer, frame.MessageNumber, frame.RpcOperation);
            var responseTask = this.StreamHandler(request, serviceProvider, responseWriter, context);

            return HandleStreamResponse(responseTask, responseWriter);
        }

        private static async Task HandleStreamResponse(ValueTask responseTask, StreamingResponseWriter<TStreamResponse> responseWriter)
        {
            //try
            //{
            await responseTask.ContextFree();
            await responseWriter.EndAsync().ContextFree();
            //            }
            //#pragma warning disable CA1031 // Do not catch general exception types
            //            catch (Exception x)
            //            {
            //                HandleResponse
            //                throw new NotImplementedException();

            //            }
            //#pragma warning restore CA1031 // Do not catch general exception types
        }
    }

    internal class LightweightClientStreamingMethodStub<TRequest, TStreamItem, TResponse> : LightweightMethodStub
        where TRequest : class, IObjectRequest
        where TResponse : class
    {
        private readonly IRpcSerializer<TRequest> requestSerializer;

        private readonly IRpcSerializer<TStreamItem> streamItemSerializer;

        private readonly IRpcSerializer<TResponse> responseSerializer;

        /// <summary>
        /// Creates a method stub that will handle client-streaming requests.
        /// </summary>
        /// <param name="fullName">Full name of the client-streaming operation.</param>
        /// <param name="handler">Delegate that will be invoked to handle the request, receiving the uploaded stream.</param>
        public LightweightClientStreamingMethodStub(
            string fullName,
            Func<TRequest, IServiceProvider?, IAsyncEnumerable<TStreamItem>, LightweightCallContext, ValueTask<TResponse>> handler,
            IRpcSerializer serializer, RpcServerFaultHandler? faultHandler)
            : base(fullName, serializer, faultHandler)
        {
            this.Handler = handler;
            this.requestSerializer = this.Serializer.CreateTyped<TRequest>();
            this.streamItemSerializer = this.Serializer.CreateTyped<TStreamItem>();
            this.responseSerializer = this.Serializer.CreateTyped<TResponse>();
        }

        public override Type RequestType => typeof(TRequest);

        public override Type ResponseType => typeof(TResponse);

        public override bool IsClientStreaming => true;

        /// <summary>
        /// Gets the handler for a client-streaming request.
        /// </summary>
        internal Func<TRequest, IServiceProvider?, IAsyncEnumerable<TStreamItem>, LightweightCallContext, ValueTask<TResponse>> Handler { get; }

        public override Task HandleMessage(ILightweightRpcFrameWriter pipeline, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context)
        {
            throw new NotSupportedException("Client-streaming operations must be handled using BeginClientStreaming.");
        }

        public override Task HandleStreamingMessage(ILightweightRpcFrameWriter pipeline, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context)
        {
            throw new NotSupportedException("Client-streaming operations must be handled using BeginClientStreaming.");
        }

        public override IClientStreamFeed BeginClientStreaming(ILightweightRpcFrameWriter pipeline, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context)
        {
            TRequest? request;
            try
            {
                request = this.requestSerializer.Deserialize(frame.Payload, null);
            }
            catch (Exception ex)
            {
                throw new RpcFailureException(RpcFailure.InvalidData, $"Payload deserialization failed. {ex.Message}", ex);
            }

            if (request == null) throw new RpcFailureException(RpcFailure.InvalidData, "Payload deserialization failed");

            return new ClientStreamFeed(this, pipeline, frame.MessageNumber, frame.RpcOperation, request, serviceProvider, context);
        }

        private Task WriteResponse(int messageNumber, string operation, ILightweightRpcFrameWriter frameWriter, TResponse response)
        {
            var headers = ImmutableArray<KeyValuePair<string, ImmutableArray<byte>>>.Empty;
            var responseHeader = new LightweightRpcFrame(RpcFrameType.UnaryResponse, messageNumber, operation, headers);

            var writeState = frameWriter.BeginWrite(responseHeader);
            BufferWriterStream responseStream = writeState.Writer;
            try
            {
                this.responseSerializer.Serialize(responseStream, response);
            }
            catch
            {
                frameWriter.AbortWrite(writeState);
                throw;
            }

            var endWriteTask = frameWriter.EndWriteAsync(writeState, false);
            return endWriteTask.IsCompletedSuccessfully ? Task.CompletedTask : endWriteTask.AsTask();
        }

        private sealed class ClientStreamFeed : IClientStreamFeed
        {
            private const int MaxQueuedItems = 16; // TODO: Configurable max queue size?

            private readonly LightweightClientStreamingMethodStub<TRequest, TStreamItem, TResponse> stub;

            private readonly ILightweightRpcFrameWriter frameWriter;

            private readonly int messageNumber;

            private readonly string operation;

            private readonly TRequest request;

            private readonly IServiceProvider? serviceProvider;

            private readonly LightweightCallContext context;

            private readonly Channel<TStreamItem> channel;

            // Signalled when the handler has completed (or faulted). Used to release any
            // pending or subsequent channel writes so that the receive loop is not blocked
            // when the handler stops reading the uploaded stream early.
            private readonly CancellationTokenSource handlerCompletedCts = new CancellationTokenSource();

            internal ClientStreamFeed(
                LightweightClientStreamingMethodStub<TRequest, TStreamItem, TResponse> stub,
                ILightweightRpcFrameWriter frameWriter,
                int messageNumber, string operation, TRequest request,
                IServiceProvider? serviceProvider, LightweightCallContext context)
            {
                this.stub = stub;
                this.frameWriter = frameWriter;
                this.messageNumber = messageNumber;
                this.operation = operation;
                this.request = request;
                this.serviceProvider = serviceProvider;
                this.context = context;

                this.channel = Channel.CreateBounded<TStreamItem>(new BoundedChannelOptions(MaxQueuedItems)
                {
                    SingleReader = true,
                    SingleWriter = true
                });
            }

            public ValueTask WriteDataAsync(in LightweightRpcFrame frame)
            {
                if (this.handlerCompletedCts.IsCancellationRequested)
                {
                    // The handler has already finished and is no longer reading the uploaded
                    // stream. Discard any further uploaded items so that the receive loop is
                    // not blocked on a full channel that will never be drained.
                    return default;
                }

                TStreamItem? item;
                try
                {
                    item = this.stub.streamItemSerializer.Deserialize(frame.Payload);
                }
                catch (Exception ex)
                {
                    throw new RpcFailureException(RpcFailure.InvalidData, $"Stream payload deserialization failed. {ex.Message}", ex);
                }

                return this.WriteItemAsync(item!);
            }

            private ValueTask WriteItemAsync(TStreamItem item)
            {
                try
                {
                    var writeTask = this.channel.Writer.WriteAsync(item, this.handlerCompletedCts.Token);
                    if (!writeTask.IsCompletedSuccessfully) return AwaitWriteAsync(writeTask);
                }
                catch (OperationCanceledException) when (this.handlerCompletedCts.IsCancellationRequested)
                {
                    // The handler completed while this item was waiting to be written to a
                    // full channel. The item is no longer needed, so it is safely discarded.
                }
                catch (ChannelClosedException)
                {
                    // The channel was completed (e.g. the handler finished). Discard the item.
                }

                return default;

                async ValueTask AwaitWriteAsync(ValueTask task)
                {
                    try
                    {
                        await task.ContextFree();
                    }
                    catch (OperationCanceledException) when (this.handlerCompletedCts.IsCancellationRequested)
                    {
                        // The handler completed while this item was waiting to be written to a
                        // full channel. The item is no longer needed, so it is safely discarded.
                    }
                    catch (ChannelClosedException)
                    {
                        // The channel was completed (e.g. the handler finished). Discard the item.
                    }
                }
            }

            public void CompleteData()
            {
                this.channel.Writer.TryComplete();
            }

            public void Fault(Exception exception)
            {
                this.channel.Writer.TryComplete(exception);
            }

            public async Task RunAsync()
            {
                try
                {
                    var response = await this.stub.Handler(
                        this.request, this.serviceProvider, this.channel.Reader.ReadAllAsync(this.context.CancellationToken), this.context).ContextFree();

                    await this.stub.WriteResponse(this.messageNumber, this.operation, this.frameWriter, response).ContextFree();
                }
                finally
                {
                    // Release any pending or subsequent uploaded items so that the receive
                    // loop is never blocked waiting to write to a channel that the handler
                    // is no longer reading.
                    await this.handlerCompletedCts.CancelAsync().ContextFree();
                    this.channel.Writer.TryComplete();
                }
            }

            public void Dispose()
            {
                Debug.Assert(this.handlerCompletedCts.IsCancellationRequested);
                this.handlerCompletedCts.Dispose();
            }
        }
    }
}