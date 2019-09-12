﻿#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using SciTech.Rpc.Client;
using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Grpc.Internal;
using SciTech.Rpc.Internal;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GrpcCore = Grpc.Core;

#if FEATURE_NET_GRPC
namespace SciTech.Rpc.NetGrpc.Client.Internal
#else
namespace SciTech.Rpc.Grpc.Client.Internal
#endif
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

        protected override ValueTask<IAsyncStreamingServerCall<TResponse>> CallStreamingMethodAsync<TRequest, TResponse>(TRequest request, GrpcProxyMethod method, CancellationToken cancellationToken)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));

            DateTime? deadline = this.GetStreamingCallDeadline();
            var callOptions = new GrpcCore.CallOptions(deadline: deadline, cancellationToken: cancellationToken);

            var typedMethod = this.grpcMethodsCache.GetGrpcMethod<TRequest, TResponse>(method);

#pragma warning disable CA2000 // Dispose objects before losing scope
            return new ValueTask<IAsyncStreamingServerCall<TResponse>>(
                new GrpcAsyncServerStreamingCall<TResponse>(this.grpcInvoker.AsyncServerStreamingCall(typedMethod, null, callOptions, request)));
#pragma warning restore CA2000 // Dispose objects before losing scope
        }

        protected override TResponse CallUnaryMethodImpl<TRequest, TResponse>(GrpcProxyMethod method, TRequest request, CancellationToken cancellationToken)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));

            DateTime? deadline = this.GetCallDeadline();
            var callOptions = new GrpcCore.CallOptions(deadline: deadline, cancellationToken: cancellationToken);

            var typedMethod = this.grpcMethodsCache.GetGrpcMethod<TRequest, TResponse>(method);

            var response = this.grpcInvoker.BlockingUnaryCall(typedMethod, null, callOptions, request);
            return response;
        }

        protected override async Task<TResponse> CallUnaryMethodImplAsync<TRequest, TResponse>(GrpcProxyMethod method, TRequest request, CancellationToken cancellationToken)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));

            DateTime? deadline = this.GetCallDeadline();
            var callOptions = new GrpcCore.CallOptions(deadline: deadline, cancellationToken: cancellationToken);

            var typedMethod = this.grpcMethodsCache.GetGrpcMethod<TRequest, TResponse>(method);
            using (var asyncCall = this.grpcInvoker.AsyncUnaryCall(typedMethod, null, callOptions, request))
            {
                var response = await asyncCall.ResponseAsync.ContextFree();
                // TODO: Handle response.Status
                return response;
            }
        }

        protected override GrpcProxyMethod CreateDynamicMethodDef<TRequest, TResponse>(string serviceName, string operationName)
        {
            return CreateMethodDef<TRequest, TResponse>(RpcMethodType.Unary, serviceName, operationName, this.Serializer, null);
        }

        protected override void HandleCallException(Exception e)
        {
            if (e != null && e is GrpcCore.RpcException rpcException)
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
                        throw new RpcFailureException(RpcFailure.Unknown, e.Message, e);
                }
            }
        }

        protected override bool IsCancellationException(Exception exception)
        {
            if (exception is GrpcCore.RpcException rpcException)
            {
                return rpcException.StatusCode == GrpcCore.StatusCode.Cancelled;
            }

            return base.IsCancellationException(exception);
        }

        private DateTime? GetCallDeadline()
        {
            var callTimeOut = this.Connection.Options.CallTimeout;
            if (callTimeOut != null)
            {
                return DateTime.UtcNow + callTimeOut;
            }

            return null;
        }

        private DateTime? GetStreamingCallDeadline()
        {
            var callTimeOut = this.Connection.Options.StreamingCallTimeout;
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

            internal GrpcAsyncServerStreamingCall(GrpcCore.AsyncServerStreamingCall<TResponse> grpcCall)
            {
                this.grpcCall = grpcCall;
                this.ResponseStream = new AsyncStreamWrapper(this.grpcCall.ResponseStream);
            }

            public IAsyncEnumerator<TResponse> ResponseStream { get; }

            public void Dispose()
            {
                this.grpcCall.Dispose();
            }

            private class AsyncStreamWrapper : IAsyncEnumerator<TResponse>
            {
                private GrpcCore.IAsyncStreamReader<TResponse> reader;

                public AsyncStreamWrapper(GrpcCore.IAsyncStreamReader<TResponse> reader)
                {
                    this.reader = reader;
                }

                public TResponse Current => this.reader.Current;

                public ValueTask DisposeAsync() => default;

                public ValueTask<bool> MoveNextAsync()
                {
                    var task = this.reader.MoveNext(CancellationToken.None);
                    if (task.Status == TaskStatus.RanToCompletion)
                    {
                        return new ValueTask<bool>(task.Result);
                    }

                    async ValueTask<bool> AwaitNext() => await task.ContextFree();

                    return AwaitNext();
                }
            }
        }
    }


    public abstract class GrpcProxyMethod : RpcProxyMethod
    {
        internal readonly string MethodName;

        internal readonly GrpcCore.MethodType MethodType;

        internal readonly string ServiceName;

        public GrpcProxyMethod(
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
