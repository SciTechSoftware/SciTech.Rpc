#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB
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
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server.Internal
{
    internal class LightweightCallContext : IRpcServerContext, IRpcServerContextBuilder
    {
        private readonly IReadOnlyCollection<KeyValuePair<string, ImmutableArray<byte>>>? headers;

        public LightweightCallContext(LightweightRpcEndPoint endPoint, IPrincipal? user, IReadOnlyCollection<KeyValuePair<string, ImmutableArray<byte>>>? headers, CancellationToken cancellationToken)
        {
            this.EndPoint = endPoint;
            this.User = user;
            this.CancellationToken = cancellationToken;
            this.headers = headers;
        }

        public IPrincipal? User { get; set; }

        public CancellationToken CancellationToken { get; }

        public LightweightRpcEndPoint EndPoint { get; private set; }

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

        public abstract Task HandleMessage(ILightweightRpcFrameWriter pipeline, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context);

        public abstract Task HandleStreamingMessage(ILightweightRpcFrameWriter pipeline, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context);
    }

    internal class LightweightMethodStub<TRequest, TResponse> : LightweightMethodStub
        where TRequest : class, IObjectRequest
        where TResponse : class
    {
        private readonly IRpcSerializer<TRequest> requestSerializer;

        private readonly IRpcSerializer<TResponse> responseSerializer;

        /// <summary>
        /// Creates a method stub that will handle streaming requests.
        /// </summary>
        /// <param name="fullName">Full name of unary operation.</param>
        /// <param name="streamHandler">Delegate that will be invoked to handle the request.</param>
        public LightweightMethodStub(
            string fullName,
            Func<TRequest, IServiceProvider?, IRpcAsyncStreamWriter<TResponse>, LightweightCallContext, ValueTask> streamHandler,
            IRpcSerializer serializer, RpcServerFaultHandler? faultHandler)
            : base(fullName, serializer, faultHandler)
        {
            this.StreamHandler = streamHandler;
            this.requestSerializer = this.Serializer.CreateTyped<TRequest>();
            this.responseSerializer = this.Serializer.CreateTyped<TResponse>();
        }

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
        /// Gets the handler for a unary request. Only one of <see cref="Handler"/> and <see cref="StreamHandler"/> will be non-<c>null</c>.
        /// </summary>
        internal Func<TRequest, IServiceProvider?, LightweightCallContext, ValueTask<TResponse>>? Handler { get; }

        /// <summary>
        /// Gets the handler for a streaming request. Only one of <see cref="Handler"/> and <see cref="StreamHandler"/> will be non-<c>null</c>.
        /// </summary>
        internal Func<TRequest, IServiceProvider?, IRpcAsyncStreamWriter<TResponse>, LightweightCallContext, ValueTask>? StreamHandler { get; }

        public override Task HandleMessage(ILightweightRpcFrameWriter frameWriter, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context)
        {
            if (this.Handler == null)
            {
                throw new RpcFailureException(RpcFailure.RemoteDefinitionError, $"Unary request handler is not initialized for '{frame.RpcOperation}'.");
            }

            TRequest? request = this.requestSerializer.Deserialize(frame.Payload, null);
            if (request == null) throw new RpcFailureException(RpcFailure.InvalidData);

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
                var responseTask = stub.Handler!(request, serviceProvider, context);

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
            TRequest? request = this.requestSerializer.Deserialize(frame.Payload);
            if( request == null ) throw new RpcFailureException(RpcFailure.InvalidData);

            var responseWriter = new StreamingResponseWriter<TResponse>(pipeline, this.responseSerializer, frame.MessageNumber, frame.RpcOperation);

            if (this.StreamHandler == null)
            {
                throw new RpcFailureException(RpcFailure.RemoteDefinitionError, $"Server streaming request handler is not initialized for '{frame.RpcOperation}'.");
            }

            var responseTask = this.StreamHandler(request, serviceProvider, responseWriter, context);

            return HandleStreamResponse(responseTask, responseWriter);
        }

        private static async Task AwaitValueTaskAsTask(ValueTask valueTask)
        {
            await valueTask.ContextFree();
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
                    return AwaitValueTaskAsTask(endWriteTask);
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

        private static async Task HandleStreamResponse(ValueTask responseTask, StreamingResponseWriter<TResponse> responseWriter)
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

}
