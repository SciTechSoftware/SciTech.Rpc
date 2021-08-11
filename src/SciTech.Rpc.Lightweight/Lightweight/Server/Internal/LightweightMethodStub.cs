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
            TRequest? request = this.requestSerializer.Deserialize(frame.Payload);
            if (request == null) throw new RpcFailureException(RpcFailure.InvalidData);

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
        internal Func<TRequest, IServiceProvider?, LightweightCallContext, ValueTask<TResponse>> Handler { get; set; }

        public override Task HandleMessage(ILightweightRpcFrameWriter frameWriter, in LightweightRpcFrame frame, IServiceProvider? serviceProvider, LightweightCallContext context)
        {
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
    [RpcService]
    public interface ISimpleService
    {
        int Add(int a, int b);

        Task<int> AddAsync(int a, int b, CancellationToken cancellationToken);

        IOtherService GetOtherService();
    }

    [RpcService]
    public interface IOtherService
    {
        int SomethingElse();
    }



    internal class LightweightBlockingMethodStub<TService,TRequest, TResult,TResponse> : LightweightMethodStub<TRequest, RpcResponse<TResponse>>
        where TService : class
        where TRequest : class, IObjectRequest
    {
        RpcStub<TService> serviceStub;
        Func<TService,TRequest, CancellationToken, TResult> implCaller;
        private Func<TResult, TResponse>? responseConverter;

        public LightweightBlockingMethodStub(
            RpcStub<TService> serviceStub,
            string fullName,
            Func<TService, TRequest, CancellationToken, TResult> implCaller,
            IRpcSerializer serializer,
            Func<RpcStub<TService>, TResult, TResponse>? responseConverter,
            RpcServerFaultHandler? faultHandler,
            bool allowInlineExecution)
            : base(fullName,
                  null,
                  serializer, faultHandler, allowInlineExecution)
        {
            this.serviceStub = serviceStub;
            this.implCaller = implCaller;
            this.responseConverter = responseConverter;
            this.Handler = Call;
        }
        //public LightweightBlockingMethodStub(
        //    RpcStub<TService> serviceStub,
        //    string fullName,
        //    Func<TService, TRequest, CancellationToken, Task<TResult>> implCaller,
        //    IRpcSerializer serializer,
        //    Func<TResult, TResponse>? responseConverter,
        //    RpcServerFaultHandler? faultHandler,
        //    bool allowInlineExecution)
        //    : base(fullName,
        //          null,
        //          serializer, faultHandler, allowInlineExecution)
        //{
        //    this.serviceStub = serviceStub;
        //    this.implCaller = implCaller;
        //    this.responseConverter = responseConverter;
        //    this.Handler = Call;
        //}

        private ValueTask<RpcResponse<TResponse>> Call(TRequest request, IServiceProvider? serviceProvider, LightweightCallContext context)
        {
            return this.serviceStub.CallBlockingMethod<TRequest, TResult, TResponse>(
                request, context,
                implCaller,
                null,
                this.FaultHandler,
                this.Serializer,
                serviceProvider);

        }
    }

    class SimpleServiceStub
    {

        public static void CreateServiceStub(IRpcServerCore server, ImmutableRpcServerOptions options, ILightweightMethodBinder binder)
        {
            var serviceStub = new RpcStub<ISimpleService>(server, options);

            binder.AddMethod(
                new LightweightBlockingMethodStub<ISimpleService, RpcObjectRequest<int, int>, int, int>(
                    serviceStub,
                    "SciTech.Rpc.SimpleService.Add",
                    (service, request, cancellationToken) => service.Add(request.Value1, request.Value2),
                    serviceStub.Serializer,
                    null,
                    null,
                    false));
            binder.AddMethod(
                new LightweightBlockingMethodStub<ISimpleService, RpcObjectRequest<int, int>, int, int>(
                    serviceStub,
                    "SciTech.Rpc.SimpleService.Add",
                    (service, request, cancellationToken) => service.Add(request.Value1, request.Value2),
                    serviceStub.Serializer,
                    null,
                    null,
                    false));
            binder.AddMethod(
                new LightweightBlockingMethodStub<ISimpleService, RpcObjectRequest, IOtherService, RpcObjectRef<IOtherService>>(
                    serviceStub,
                    "SciTech.Rpc.SimpleService.GetOtherService",
                    (service, request, cancellationToken) => service.GetOtherService(),
                    serviceStub.Serializer,
                    (stub, s)=>stub.ConvertServiceResponse(s),
                    null,
                    false));
        }

    }
}
