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

using SciTech.IO;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server.Internal
{
    internal class LightweightCallContext : IRpcCallContext, IRpcServerCallMetadata
    {
        private readonly IReadOnlyCollection<KeyValuePair<string, string>>? headers;

        public LightweightCallContext(IReadOnlyCollection<KeyValuePair<string, string>>? headers, CancellationToken cancellationToken)
        {
            this.CancellationToken = cancellationToken;
            this.headers = headers;
        }

        public CancellationToken CancellationToken { get; }

        public IRpcServerCallMetadata RequestHeaders => this;

        public string? GetValue(string key)
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

        public abstract Task HandleMessage(RpcPipeline pipeline, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context);

        public abstract Task HandleStreamingMessage(RpcPipeline pipeline, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context);
    }

    internal class LightweightMethodStub<TRequest, TResponse> : LightweightMethodStub
        where TRequest : class
        where TResponse : class
    {
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

        public override Task HandleMessage(RpcPipeline pipeline, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context)
        {
            if (this.Handler == null)
            {
                throw new RpcFailureException(RpcFailure.RemoteDefinitionError, $"Unary request handler is not initialized for '{frame.RpcOperation}'.");
            }

            TRequest request;
            using (var requestStream = frame.Payload.AsStream())
            {
                request = this.Serializer.FromStream<TRequest>(requestStream);
            }


            Task ExecuteOperation(
                RpcPipeline pipeline, TRequest request, int messageNumber, string operation,
                IServiceProvider? serviceProvider, LightweightCallContext context)
            {
                var responseTask = this.Handler!(request, serviceProvider, context);

                return this.HandleResponse(messageNumber, operation, pipeline, responseTask);
            }

            Task StartHandleMessage(RpcPipeline pipeline, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context, TRequest request)
            {
                var messageNumber = frame.MessageNumber;
                var operation = frame.RpcOperation;
                // TODO: Scheduler should be a stub property
                var scheduler = TaskScheduler.Default;

                return Task.Factory.StartNew(
                    () => ExecuteOperation(pipeline, request, messageNumber, operation, serviceProvider, context),
                    context.CancellationToken,
                    TaskCreationOptions.DenyChildAttach, scheduler).Unwrap();
            }


            if (this.AllowInlineExecution)
            {
                return ExecuteOperation(pipeline, request, frame.MessageNumber, frame.RpcOperation, serviceProvider, context);
            }
            else
            {
                return StartHandleMessage(pipeline, frame, serviceProvider, context, request);
            }

        }

        public override Task HandleStreamingMessage(RpcPipeline pipeline, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context)
        {
            TRequest request;
            using (var requestStream = frame.Payload.AsStream())
            {
                request = this.Serializer.FromStream<TRequest>(requestStream);
            }

            var responseWriter = new StreamingResponseWriter<TResponse>(pipeline, this.Serializer, frame.MessageNumber, frame.RpcOperation);


            if (this.StreamHandler == null)
            {
                throw new RpcFailureException(RpcFailure.RemoteDefinitionError, $"Server streaming request handler is not initialized for '{frame.RpcOperation}'.");
            }

            var responseTask = this.StreamHandler(request, serviceProvider, responseWriter, context);

            return this.HandleStreamResponse(responseTask, responseWriter);
        }

        private Task HandleResponse(int messageNumber, string operation, RpcPipeline pipeline, ValueTask<TResponse> responseTask)
        {
            // Try to return response from synchronous methods directly.
            if (responseTask.IsCompletedSuccessfully)
            {
                var response = responseTask.Result;
                ImmutableArray<KeyValuePair<string, string>> headers = ImmutableArray<KeyValuePair<string, string>>.Empty;  // TODO:
                var responseHeader = new LightweightRpcFrame(RpcFrameType.UnaryResponse, messageNumber, operation, headers);

                var responseStreamTask = pipeline.TryBeginWriteAsync(responseHeader);
                if (responseStreamTask.IsCompletedSuccessfully)
                {
                    if (responseStreamTask.Result is Stream responseStream)
                    {
                        // Perfect, everything is synchronous.
                        try
                        {
                            this.Serializer.ToStream(responseStream, response);
                        }
                        catch
                        {
                            pipeline.AbortWrite();
                            throw;
                        }

                        return pipeline.EndWriteAsync();
                    }

                    return Task.CompletedTask;
                }
                else
                {
                    // There's probably a contention for the write pipe.
                    async Task AwaitAndWriteResponse()
                    {
                        var responseStream = await responseStreamTask.ContextFree();
                        try
                        {
                            this.Serializer.ToStream(responseStream, response);
                        }
                        catch
                        {
                            pipeline.AbortWrite();
                            throw;
                        }

                        await pipeline.EndWriteAsync().ContextFree();
                    }

                    return AwaitAndWriteResponse();
                }
            }
            else
            {
                // Handler is asynchronous (or an error occurred)
                async Task AwaitAndWriteResponse(int messageNumber, string operation)
                {
                    var response = await responseTask.ContextFree();
                    ImmutableArray<KeyValuePair<string, string>> headers = ImmutableArray<KeyValuePair<string, string>>.Empty;  // TODO:
                    var responseHeader = new LightweightRpcFrame(RpcFrameType.UnaryResponse, messageNumber, operation, headers);

                    var responseStream = await pipeline.TryBeginWriteAsync(responseHeader);
                    if (responseStream != null)
                    {
                        try
                        {
                            this.Serializer.ToStream(responseStream, response);
                        }
                        catch
                        {
                            pipeline.AbortWrite();
                            throw;
                        }

                        await pipeline.EndWriteAsync().ContextFree();
                    }
                }

                return AwaitAndWriteResponse(messageNumber, operation);
            }
        }

        private async Task HandleStreamResponse(ValueTask responseTask, StreamingResponseWriter<TResponse> responseWriter)
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
