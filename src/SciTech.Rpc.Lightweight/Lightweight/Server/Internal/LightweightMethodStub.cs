﻿#region Copyright notice and license
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
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

        public abstract ValueTask HandleMessage(RpcPipeline pipeline, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context);

        public abstract ValueTask HandleStreamingMessage(RpcPipeline pipeline, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context);
    }

    internal class LightweightMethodStub<TRequest, TResponse> : LightweightMethodStub
        where TRequest : class
        where TResponse : class
    {
        /// <summary>
        /// Creates a method stub that will handle unary requests.
        /// </summary>
        /// <param name="fullName">Full name of unary operation.</param>
        /// <param name="handler">Delegate that will be invoked to handle the request.</param>
        public LightweightMethodStub(
            string fullName, 
            Func<TRequest, IServiceProvider?, LightweightCallContext, ValueTask<TResponse>> handler, 
            IRpcSerializer serializer, RpcServerFaultHandler? faultHandler)
            : base(fullName, serializer, faultHandler)
        {
            this.Handler = handler;
        }

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
        public override Type RequestType => typeof(TRequest);

        public override Type ResponseType => typeof(TResponse);

        /// <summary>
        /// Gets the handler for a unary request. Only one of <see cref="Handler"/> and <see cref="StreamHandler"/> will be non-<c>null</c>.
        /// </summary>
        internal Func<TRequest, IServiceProvider?, LightweightCallContext, ValueTask<TResponse>>? Handler { get;}

        /// <summary>
        /// Gets the handler for a streaming request. Only one of <see cref="Handler"/> and <see cref="StreamHandler"/> will be non-<c>null</c>.
        /// </summary>
        internal Func<TRequest, IServiceProvider?, IRpcAsyncStreamWriter<TResponse>, LightweightCallContext, ValueTask>? StreamHandler { get; }
        
        public override ValueTask HandleMessage(RpcPipeline pipeline, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context)
        {
            TRequest request;
            using (var requestStream = frame.Payload.AsStream())
            {
                request = this.Serializer.FromStream<TRequest>(requestStream);
            }
            
            if (this.Handler == null)
            {
                throw new RpcFailureException($"Unary request handler is not initialized for '{frame.RpcOperation}'.");
            }

            var responseTask = this.Handler(request, serviceProvider, context);

            return this.HandleResponse(frame, pipeline, responseTask);
        }

        public override ValueTask HandleStreamingMessage(RpcPipeline pipeline, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context)
        {
            TRequest request;
            using (var requestStream = frame.Payload.AsStream())
            {
                request = this.Serializer.FromStream<TRequest>(requestStream);
            }

            var responseWriter = new StreamingResponseWriter<TResponse>(pipeline, this.Serializer, frame.MessageNumber, frame.RpcOperation);


            if (this.StreamHandler == null)
            {
                throw new RpcFailureException($"Server streaming request handler is not initialized for '{frame.RpcOperation}'.");
            }

            var responseTask = this.StreamHandler(request, serviceProvider, responseWriter, context);

            return this.HandleStreamResponse(responseTask, responseWriter);
        }

        private ValueTask HandleResponse(in LightweightRpcFrame frame, RpcPipeline pipeline, ValueTask<TResponse> responseTask)
        {
            // Try to return response from synchronous methods directly.
            if (responseTask.IsCompletedSuccessfully)
            {
                var response = responseTask.Result;
                ImmutableArray<KeyValuePair<string, string>> headers = ImmutableArray<KeyValuePair<string, string>>.Empty;  // TODO:
                var responseHeader = new LightweightRpcFrame(RpcFrameType.UnaryResponse, frame.MessageNumber, frame.RpcOperation, headers);

                var responseStreamTask = pipeline.BeginWriteAsync(responseHeader);
                if (responseStreamTask.IsCompletedSuccessfully)
                {
                    // Perfect, everything is synchronous.
                    var responseStream = responseStreamTask.Result;
                    this.Serializer.ToStream(responseStream, response);
                    return pipeline.EndWriteAsync();
                }
                else
                {
                    // There's probably a contention for the write pipe.
                    async ValueTask AwaitAndWriteResponse()
                    {
                        var responseStream = await responseStreamTask.ContextFree();
                        this.Serializer.ToStream(responseStream, response);
                        await pipeline.EndWriteAsync().ContextFree();
                    }

                    return AwaitAndWriteResponse();
                }
            }
            else
            {
                // Handler is asynchronous (or an error occurred)
                async ValueTask AwaitAndWriteResponse(int messageNumber, string operation)
                {
                    var response = await responseTask.ContextFree();
                    ImmutableArray<KeyValuePair<string, string>> headers = ImmutableArray<KeyValuePair<string, string>>.Empty;  // TODO:
                    var responseHeader = new LightweightRpcFrame(RpcFrameType.UnaryResponse, messageNumber, operation, headers);

                    var responseStream = await pipeline.BeginWriteAsync(responseHeader);
                    this.Serializer.ToStream(responseStream, response);
                    await pipeline.EndWriteAsync().ContextFree();
                }

                return AwaitAndWriteResponse(frame.MessageNumber, frame.RpcOperation);
            }
        }

        private async ValueTask HandleStreamResponse(ValueTask responseTask, StreamingResponseWriter<TResponse> responseWriter)
        {
            try
            {
                await responseTask.ContextFree();
                await responseWriter.EndAsync().ContextFree();
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception)
            {
                throw new NotImplementedException();

            }
#pragma warning restore CA1031 // Do not catch general exception types

        }
    }
}