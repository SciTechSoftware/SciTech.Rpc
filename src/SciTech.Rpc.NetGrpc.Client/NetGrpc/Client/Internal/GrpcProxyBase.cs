#region Copyright notice and license
// Copyright (c) SciTech Software AB and contributors.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using SciTech.Collections;
using SciTech.Rpc.Client;
using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.NetGrpc.Internal;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Serialization;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.NetGrpc.Client.Internal
{
    public abstract class GrpcProxyBase : RpcProxyBase<GrpcProxyMethod>
    {

        private readonly GrpcCore.CallInvoker grpcInvoker;

        private readonly GrpcMethodsCache grpcMethodsCache;

        protected GrpcProxyBase(GrpcProxyArgs proxyArgs, GrpcProxyMethod[] proxyMethods) : base(proxyArgs, proxyMethods)
        {
            if (proxyArgs is null) throw new ArgumentNullException(nameof(proxyArgs));

            this.grpcInvoker = proxyArgs.CallInvoker;
            this.Serializer = proxyArgs.Serializer;
            this.grpcMethodsCache = proxyArgs.MethodsCache;
        }

        public IRpcSerializer Serializer { get; }

        /// <summary>
        /// Will be called by generated code. A RpcProxyBase implementation class must have a static 
        /// method named CreateMethodDef, with this signature.
        /// </summary>
        /// <typeparam name="TRequest"></typeparam>
        /// <typeparam name="TResponse"></typeparam>
        /// <param name="methodType"></param>
        /// <param name="serviceName"></param>
        /// <param name="methodName"></param>
        /// <param name="serializerOverride"></param>
        /// <param name="faultHandler"></param>
        /// <returns></returns>
        public static GrpcProxyMethod CreateMethodDef<TRequest, TResponse>(
            RpcMethodType methodType, string serviceName, string methodName,
            IRpcSerializer? serializerOverride,
            RpcClientFaultHandler? faultHandler)
            where TRequest : class
            where TResponse : class
        {
            GrpcCore.MethodType grpcMethodType;
            switch (methodType)
            {
                case RpcMethodType.ServerStreaming:
                    grpcMethodType = GrpcCore.MethodType.ServerStreaming;
                    break;
                case RpcMethodType.ClientStreaming:
                    // The request stream consists of RpcClientStreamingMessages, see GrpcMethodsCache.GetClientStreamingGrpcMethod.
                    grpcMethodType = GrpcCore.MethodType.ClientStreaming;
                    break;
                case RpcMethodType.EventAdd:
                    grpcMethodType = GrpcCore.MethodType.ServerStreaming;
                    break;
                case RpcMethodType.Unary:
                //case RpcMethodType.PropertyGet:
                //case RpcMethodType.PropertySet:
                case RpcMethodType.EventRemove:
                    grpcMethodType = GrpcCore.MethodType.Unary;
                    break;
                default:
                    throw new ArgumentException($"Unknown methodType 'methodType'", nameof(methodType));
            }

            return new GrpcProxyMethod<TRequest, TResponse>(grpcMethodType, serviceName, methodName, serializerOverride, faultHandler);
        }

        protected override ValueTask<IAsyncStreamingServerCall<TResponse>> CallStreamingMethodAsync<TRequest, TResponse>(TRequest request,
                                                                                                                         GrpcProxyMethod method,
                                                                                                                         bool silentCancellation,
                                                                                                                         CancellationToken cancellationToken)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));

            DateTime? deadline = this.GetStreamingCallDeadline();
            var callOptions = new GrpcCore.CallOptions(deadline: deadline, cancellationToken: cancellationToken);

            var typedMethod = this.grpcMethodsCache.GetGrpcMethod<TRequest, TResponse>(method);

#pragma warning disable CA2000 // Dispose objects before losing scope
            return new ValueTask<IAsyncStreamingServerCall<TResponse>>(
                new GrpcAsyncServerStreamingCall<TResponse>(this.grpcInvoker.AsyncServerStreamingCall(typedMethod, null, callOptions, request),
                    deadline, silentCancellation ? cancellationToken : CancellationToken.None, cancellationToken));
#pragma warning restore CA2000 // Dispose objects before losing scope
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "A failed write means that the call has completed, and the response or its status is the result of the call.")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2016:Forward the 'CancellationToken' parameter to methods",
            Justification = "The call is cancelled by the token in its call options. WriteAsync with a token is not available for all targets.")]
        protected override async Task<TResponse> CallClientStreamingMethodCoreAsync<TRequest, TStreamItem, TResponse>(GrpcProxyMethod method, TRequest request, IAsyncEnumerable<TStreamItem> requestStream, CancellationToken cancellationToken)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));
            if (requestStream is null) throw new ArgumentNullException(nameof(requestStream));

            DateTime? deadline = this.GetStreamingCallDeadline();
            var callOptions = new GrpcCore.CallOptions(deadline: deadline, cancellationToken: cancellationToken);

            var typedMethod = this.grpcMethodsCache.GetClientStreamingGrpcMethod<TRequest, TStreamItem, TResponse>(method);

            // Disposing the call cancels it if it hasn't completed, so the server handler is not left waiting for the
            // rest of the request stream, e.g. if the request stream fails.
            using var call = this.grpcInvoker.AsyncClientStreamingCall(typedMethod, null, callOptions);
            try
            {
                await WriteRequestStreamAsync().ContextFree();

                return await call.ResponseAsync.ContextFree();
            }
            catch (GrpcCore.RpcException e) when (IsDeadlineFailure(e, deadline, cancellationToken))
            {
                throw CreateDeadlineExceededException(e);
            }

            async Task WriteRequestStreamAsync()
            {
                var writer = call.RequestStream;

                // A write fails if the call has already completed, e.g. because the server handler returned without reading
                // the whole stream, or failed. Then the response, or its status, is the result of the call.
                if (!await TryWriteAsync(new RpcClientStreamingMessage<TRequest, TStreamItem> { Request = request }).ContextFree())
                {
                    return;
                }

                var enumerator = requestStream.GetAsyncEnumerator(cancellationToken);
                try
                {
                    while (!call.ResponseAsync.IsCompleted)
                    {
                        bool hasNext;
                        try
                        {
                            hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                        }
                        catch (Exception e) when (!(e is OperationCanceledException && cancellationToken.IsCancellationRequested))
                        {
                            // The request stream of the caller failed. Like the Lightweight implementation, the failure is
                            // reported as an RpcFailureException, with the original exception as the inner exception.
                            throw new RpcFailureException(RpcFailure.Unknown, $"The request stream failed. {e.Message}", e);
                        }

                        if (!hasNext)
                        {
                            break;
                        }

                        if (!await TryWriteAsync(new RpcClientStreamingMessage<TRequest, TStreamItem> { Item = enumerator.Current }).ContextFree())
                        {
                            return;
                        }
                    }
                }
                finally
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                }

                if (!call.ResponseAsync.IsCompleted)
                {
                    try
                    {
                        await writer.CompleteAsync().ContextFree();
                    }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        // As for a failed write.
                    }
                }

                async Task<bool> TryWriteAsync(RpcClientStreamingMessage<TRequest, TStreamItem> message)
                {
                    try
                    {
                        await writer.WriteAsync(message).ContextFree();
                        return true;
                    }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }
                }
            }
        }

        protected override TResponse CallUnaryMethodCore<TRequest, TResponse>(GrpcProxyMethod methodDef, TRequest request, CancellationToken cancellationToken)
        {
            if (methodDef is null) throw new ArgumentNullException(nameof(methodDef));

            DateTime? deadline = this.GetCallDeadline();
            var callOptions = new GrpcCore.CallOptions(deadline: deadline, cancellationToken: cancellationToken);

            var typedMethod = this.grpcMethodsCache.GetGrpcMethod<TRequest, TResponse>(methodDef);

            try
            {
                return this.grpcInvoker.BlockingUnaryCall(typedMethod, null, callOptions, request);
            }
            catch (GrpcCore.RpcException e) when (IsDeadlineFailure(e, deadline, cancellationToken))
            {
                throw CreateDeadlineExceededException(e);
            }
        }

        protected override async Task<TResponse> CallUnaryMethodCoreAsync<TRequest, TResponse>(GrpcProxyMethod methodDef, TRequest request, CancellationToken cancellationToken)
        {
            if (methodDef is null) throw new ArgumentNullException(nameof(methodDef));

            DateTime? deadline = this.GetCallDeadline();
            var callOptions = new GrpcCore.CallOptions(deadline: deadline, cancellationToken: cancellationToken);

            var typedMethod = this.grpcMethodsCache.GetGrpcMethod<TRequest, TResponse>(methodDef);
            using (var asyncCall = this.grpcInvoker.AsyncUnaryCall(typedMethod, null, callOptions, request))
            {
                try
                {
                    var response = await asyncCall.ResponseAsync.ContextFree();
                    // TODO: Handle response.Status
                    return response;
                }
                catch (GrpcCore.RpcException e) when (IsDeadlineFailure(e, deadline, cancellationToken))
                {
                    throw CreateDeadlineExceededException(e);
                }
            }
        }

        protected override GrpcProxyMethod CreateDynamicMethodDef<TRequest, TResponse>(string serviceName, string operationName)
        {
            return CreateMethodDef<TRequest, TResponse>(RpcMethodType.Unary, serviceName, operationName, this.Serializer, null);
        }

        protected override void HandleCallException(GrpcProxyMethod methodDef, Exception e)
        {
            Contract.Requires(methodDef != null);
            Contract.Requires(e != null);

            if (e is GrpcCore.RpcException rpcException)
            {
                switch (rpcException.StatusCode)
                {
                    case GrpcCore.StatusCode.Unavailable:
                        throw new RpcCommunicationException(RpcCommunicationStatus.Unavailable, e.Message, e);
                    case GrpcCore.StatusCode.ResourceExhausted:
                        throw new RpcFailureException(RpcFailure.SizeLimitExceeded, e.Message, e);
                    case GrpcCore.StatusCode.Cancelled:
                        throw new OperationCanceledException(e.Message, e);
                    case GrpcCore.StatusCode.DeadlineExceeded:
                        throw new TimeoutException(e.Message, e);
                    default:
                        this.HandleErrorException(methodDef!, e, rpcException);
                        break;
                }                
            }
        }

        private void HandleErrorException(GrpcProxyMethod method, Exception e, GrpcCore.RpcException rpcException)
        {
            string? errorType = null;
            string? message = null;
            string? faultCode = null; ;
            byte[]? faultDetails = null;
            byte[]? serializedErrorInfo = null;
            foreach (var entry in rpcException.Trailers)
            {
                switch (entry.Key)
                {
                    case WellKnownHeaderKeys.ErrorType:
                        errorType = entry.Value;
                        break;
                    case WellKnownHeaderKeys.ErrorMessage:
                        message = entry.Value;
                        break;
                    case WellKnownHeaderKeys.ErrorCode:
                        faultCode = entry.Value;
                        break;
                    case WellKnownHeaderKeys.ErrorDetails:
                        faultDetails = entry.ValueBytes;
                        break;
                    case WellKnownHeaderKeys.ErrorInfo:
                        serializedErrorInfo = entry.ValueBytes;
                        break;
                }

                if (serializedErrorInfo != null)
                {
                    // Serialized error info takes precedence, so no need to look for other error keys.
                    break;
                }
            }

            RpcError? rpcError = null;
            if (serializedErrorInfo != null)
            {
                var actualSerializer = method.SerializerOverride ?? this.serializer;
                rpcError = actualSerializer.Deserialize<RpcError>(serializedErrorInfo);
            }
            else if (!string.IsNullOrEmpty(errorType))
            {
                rpcError = new RpcError { ErrorType = errorType, ErrorCode = faultCode, Message = message, ErrorDetails = faultDetails };
            }

            if (rpcError != null)
            {
                this.HandleRpcError(method, rpcError);
            }

            throw new RpcFailureException(RpcFailure.Unknown, e.Message, e);
        }

        protected override bool IsCancellationException(Exception exception)
        {
            if (exception is GrpcCore.RpcException rpcException)
            {
                return rpcException.StatusCode == GrpcCore.StatusCode.Cancelled;
            }

            return base.IsCancellationException(exception);
        }

        /// <summary>
        /// Gets a value indicating whether <paramref name="e"/> is a transport failure of a call whose deadline has passed.
        /// When the server enforces the deadline, it may abort the call before the client detects the deadline, and the
        /// client then reports e.g. <see cref="GrpcCore.StatusCode.Unavailable"/> instead of
        /// <see cref="GrpcCore.StatusCode.DeadlineExceeded"/>. Which status is reported depends on timing and platform.
        /// </summary>
        private static bool IsDeadlineFailure(GrpcCore.RpcException e, DateTime? deadline, CancellationToken cancellationToken)
        {
            switch (e.StatusCode)
            {
                case GrpcCore.StatusCode.Unavailable:
                case GrpcCore.StatusCode.Internal:
                case GrpcCore.StatusCode.Cancelled:
                    return deadline != null && DateTime.UtcNow >= deadline && !cancellationToken.IsCancellationRequested;
                default:
                    return false;
            }
        }

        private static GrpcCore.RpcException CreateDeadlineExceededException(GrpcCore.RpcException e)
            => new GrpcCore.RpcException(new GrpcCore.Status(GrpcCore.StatusCode.DeadlineExceeded, "Deadline exceeded.", e));

        private DateTime? GetCallDeadline()
        {
            var callTimeOut = this.Channel.Options.CallTimeout;
            if (callTimeOut != null)
            {
                return DateTime.UtcNow + callTimeOut;
            }

            return null;
        }

        private DateTime? GetStreamingCallDeadline()
        {
            var callTimeOut = this.Channel.Options.StreamingCallTimeout;
            if (callTimeOut != null)
            {
                return DateTime.UtcNow + callTimeOut;
            }

            return null;
        }

        private sealed class GrpcAsyncServerStreamingCall<TResponse> : IAsyncStreamingServerCall<TResponse>
            where TResponse : class
        {

            private GrpcCore.AsyncServerStreamingCall<TResponse> grpcCall;

            internal GrpcAsyncServerStreamingCall(GrpcCore.AsyncServerStreamingCall<TResponse> grpcCall, DateTime? deadline,
                CancellationToken silentCancellationToken, CancellationToken cancellationToken)
            {
                this.grpcCall = grpcCall;
                this.ResponseStream = new AsyncStreamWrapper(this.grpcCall.ResponseStream, deadline, silentCancellationToken, cancellationToken);
            }

            public IAsyncEnumerator<TResponse> ResponseStream { get; }

            public ValueTask DisposeAsync()
            {
                this.grpcCall.Dispose();
                return default;
            }

            private class AsyncStreamWrapper : IAsyncEnumerator<TResponse>
            {
                private GrpcCore.IAsyncStreamReader<TResponse> reader;

                /// <summary>
                /// Cancelling this token ends the stream instead of throwing. Only the caller's cancellation is silent, so that
                /// e.g. an authorization failure, which is also reported as Cancelled, still surfaces as an error.
                /// </summary>
                private CancellationToken silentCancellationToken;

                private readonly DateTime? deadline;

                private readonly CancellationToken cancellationToken;

                public AsyncStreamWrapper(GrpcCore.IAsyncStreamReader<TResponse> reader, DateTime? deadline,
                    CancellationToken silentCancellationToken, CancellationToken cancellationToken)
                {
                    this.reader = reader;
                    this.silentCancellationToken = silentCancellationToken;
                    this.deadline = deadline;
                    this.cancellationToken = cancellationToken;
                }

                public TResponse Current => this.reader.Current;

                public ValueTask DisposeAsync() => default;

                [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "RanToCompletion")]
                public ValueTask<bool> MoveNextAsync()
                {
                    var task = this.reader.MoveNext(CancellationToken.None);
                    if (task.Status == TaskStatus.RanToCompletion)
                    {
                        return new ValueTask<bool>(task.Result);
                    }

                    async ValueTask<bool> AwaitNext()
                    {
                        try
                        {
                            return await task.ContextFree();
                        }
                        catch (Exception e) when (this.silentCancellationToken.IsCancellationRequested && IsCancellation(e))
                        {
                            // A silently cancelled stream just ends, like the Lightweight implementation.
                            return false;
                        }
                        catch (GrpcCore.RpcException e) when (IsDeadlineFailure(e, this.deadline, this.cancellationToken))
                        {
                            throw CreateDeadlineExceededException(e);
                        }
                    }

                    return AwaitNext();
                }

                private static bool IsCancellation(Exception e)
                    => e is OperationCanceledException
                    || (e is GrpcCore.RpcException rpcException && rpcException.StatusCode == GrpcCore.StatusCode.Cancelled);
            }
        }
    }


    public abstract class GrpcProxyMethod : RpcProxyMethod
    {
        internal readonly string MethodName;

        internal readonly GrpcCore.MethodType MethodType;

        internal readonly string ServiceName;

        protected GrpcProxyMethod(
            GrpcCore.MethodType methodType,
            string serviceName,
            string methodName,
            IRpcSerializer? serializerOverride,
            RpcClientFaultHandler? faultHandler)
            : base(serializerOverride, faultHandler)
        {
            this.MethodType = methodType;
            this.ServiceName = serviceName;
            this.MethodName = methodName;
        }
    }

    public class GrpcProxyMethod<TRequest, TResponse> : GrpcProxyMethod
        where TRequest : class
        where TResponse : class

    {
        public GrpcProxyMethod(
            GrpcCore.MethodType methodType,
            string serviceName,
            string methodName,
            IRpcSerializer? serializerOverride,
            RpcClientFaultHandler? faultHandler)
            : base(methodType, serviceName, methodName, serializerOverride, faultHandler)
        {

        }

        protected internal override Type RequestType => typeof(TRequest);

        protected internal override Type ResponseType => typeof(TResponse);

        internal GrpcCore.Method<TRequest, TResponse> CreateMethod(IRpcSerializer serializer)
        {
            IRpcSerializer actualSerializer = this.SerializerOverride ?? serializer;
            return GrpcMethodDefinition.Create<TRequest, TResponse>(
                methodType: this.MethodType,
                serviceName: this.ServiceName,
                methodName: this.MethodName,
                actualSerializer);
        }

    }
}
