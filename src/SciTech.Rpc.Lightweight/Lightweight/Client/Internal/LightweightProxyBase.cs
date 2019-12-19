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
using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Serialization;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Client.Internal
{
    public class LightweightProxyArgs : RpcProxyArgs
    {
        internal LightweightProxyArgs(
            LightweightRpcConnection connection,
            IReadOnlyList<RpcClientCallInterceptor> callInterceptors,
            RpcObjectId objectId,
            IRpcSerializer serializer,
            LightweightSerializersCache methodSerializersCache,
            IReadOnlyCollection<string>? implementedServices,
            IRpcProxyDefinitionsProvider proxyServicesProvider,
            SynchronizationContext? syncContext)
            : base(connection, objectId, serializer, implementedServices, proxyServicesProvider, syncContext)
        {
            this.MethodSerializersCache = methodSerializersCache;
            this.CallInterceptors = callInterceptors;
        }

        internal IReadOnlyList<RpcClientCallInterceptor> CallInterceptors { get; }

        internal new LightweightRpcConnection Channel => (LightweightRpcConnection)base.Channel;

        internal LightweightSerializersCache MethodSerializersCache { get; }
    }

#pragma warning disable CA1062 // Validate arguments of public methods
    public class LightweightProxyBase : RpcProxyBase<LightweightMethodDef>
    {
        private readonly int callTimeout;

        private readonly LightweightRpcConnection connection;

        private readonly LightweightSerializersCache methodSerializersCache;

        private readonly int streamingCallTimeout;

        protected LightweightProxyBase(LightweightProxyArgs proxyArgs, LightweightMethodDef[] proxyMethods) : base(proxyArgs, proxyMethods)
        {
            this.connection = proxyArgs.Channel;
            this.callTimeout = ((int?)this.connection.Options.CallTimeout?.TotalMilliseconds) ?? 0;
            this.streamingCallTimeout = ((int?)this.connection.Options.StreamingCallTimeout?.TotalMilliseconds) ?? 0;
            this.methodSerializersCache = proxyArgs.MethodSerializersCache;
            this.CallInterceptors = proxyArgs.CallInterceptors.ToImmutableArray();
        }

        private ImmutableArray<RpcClientCallInterceptor> CallInterceptors { get; }

        // TODO: This should be be moved to a virtual method in RpcServiceProxyBuilder returning an Expression.
        public static LightweightMethodDef CreateMethodDef<TRequest, TResponse>(
            RpcMethodType methodType,
            string serviceName, string methodName,
            IRpcSerializer? serializer,
            RpcClientFaultHandler? faultHandler)
        {
            return new LightweightMethodDef<TRequest, TResponse>(methodType, $"{serviceName}.{methodName}", serializer, faultHandler);
        }


        protected override ValueTask<IAsyncStreamingServerCall<TResponse>> CallStreamingMethodAsync<TRequest, TResponse>(
            TRequest request, LightweightMethodDef method, CancellationToken ct)
        {
            IReadOnlyDictionary<string, string>? headers = this.CreateCallHeaders();

            var actualSerializers = ((LightweightMethodDef<TRequest, TResponse>)method).LightweightSerializersOverride
                ?? this.methodSerializersCache.GetSerializers<TRequest, TResponse>(method);

            var clientTask = this.ConnectCoreAsync(ct);
            if (clientTask.IsCompletedSuccessfully)
            {
                var client = clientTask.Result;
                var streamingCallTask = client.BeginStreamingServerCall(
                    RpcFrameType.StreamingRequest,
                    method.OperationName,
                    headers,
                    request,
                    actualSerializers,
                    this.streamingCallTimeout,
                    ct);

                return streamingCallTask;
            }

            async ValueTask<IAsyncStreamingServerCall<TResponse>> AwaitConnectAndCall(ValueTask<RpcPipelineClient> pendingClientTask)
            {
                var client = await pendingClientTask.ContextFree();
                var streamingCall = await client.BeginStreamingServerCall(
                    RpcFrameType.StreamingRequest,
                    method.OperationName,
                    headers,
                    request,
                    actualSerializers,
                    this.streamingCallTimeout,
                    ct).ContextFree();

                return streamingCall;
            }

            return AwaitConnectAndCall(clientTask);
        }

        protected override TResponse CallUnaryMethodImpl<TRequest, TResponse>(LightweightMethodDef methodDef, TRequest request, CancellationToken cancellationToken)
        {
            return CallUnaryMethodImplAsync<TRequest, TResponse>(methodDef, request, cancellationToken).AwaiterResult();
        }

        protected override Task<TResponse> CallUnaryMethodImplAsync<TRequest, TResponse>(LightweightMethodDef methodDef, TRequest request, CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<string, string>? headers = this.CreateCallHeaders();

            var actualSerializers = ((LightweightMethodDef<TRequest, TResponse>)methodDef).LightweightSerializersOverride
                ?? this.methodSerializersCache.GetSerializers<TRequest, TResponse>(methodDef);

            var clientTask = this.ConnectCoreAsync(cancellationToken);
            if (clientTask.IsCompletedSuccessfully)
            {
                var client = clientTask.Result;
                var responseTask = client.SendReceiveFrameAsync2<TRequest, TResponse>(
                    RpcFrameType.UnaryRequest,
                    methodDef.OperationName,
                    headers,
                    request,
                    actualSerializers,
                    this.callTimeout,
                    cancellationToken);

                return responseTask;
            }

            async Task<TResponse> AwaitConnectAndCall(ValueTask<RpcPipelineClient> pendingClientTask)
            {
                var client = await pendingClientTask.ContextFree();
                return await client.SendReceiveFrameAsync2<TRequest, TResponse>(
                    RpcFrameType.UnaryRequest,
                    methodDef.OperationName,
                    headers,
                    request,
                    actualSerializers,
                    this.callTimeout,
                    cancellationToken).ContextFree();
            }

            return AwaitConnectAndCall(clientTask);
        }

        protected override LightweightMethodDef CreateDynamicMethodDef<TRequest, TResponse>(string serviceName, string operationName)
        {
            return CreateMethodDef<TRequest, TResponse>(RpcMethodType.Unary, serviceName, operationName, this.serializer, null);
        }

        protected override void HandleCallException(Exception e)
        {
            switch (e)
            {
                case Pipelines.Sockets.Unofficial.ConnectionResetException _:
                    throw new RpcCommunicationException(RpcCommunicationStatus.ConnectionLost, e.Message, e);
                case Pipelines.Sockets.Unofficial.ConnectionAbortedException _:
                    throw new RpcCommunicationException(RpcCommunicationStatus.Unavailable, e.Message, e);
                case SocketException socketException:
                    switch (socketException.SocketErrorCode)
                    {
                        case SocketError.ConnectionAborted:
                        case SocketError.ConnectionRefused:
                        case SocketError.ConnectionReset:
                        case SocketError.HostDown:
                        case SocketError.HostNotFound:
                        case SocketError.HostUnreachable:
                            throw new RpcCommunicationException(RpcCommunicationStatus.Unavailable, e.Message, e);
                        default:
                            throw new RpcCommunicationException(RpcCommunicationStatus.Unknown);
                    }
                case IOException ioe:
                    throw new RpcCommunicationException(RpcCommunicationStatus.Unknown, ioe.Message);
                case RpcCommunicationException _:
                case RpcFailureException _:
                case OperationCanceledException _:
                    break;
                case TimeoutException _:
                    break;
                default:
                    throw new RpcFailureException(RpcFailure.Unknown, $"Unexepected exception when calling RPC method. {e.Message}", e);
            }
        }

        private ValueTask<RpcPipelineClient> ConnectCoreAsync(CancellationToken cancellationToken)
        {
            return this.connection.ConnectClientAsync(cancellationToken);
        }

        private IReadOnlyDictionary<string, string>? CreateCallHeaders()
        {
            IReadOnlyDictionary<string, string>? headers = null;
            int nInterceptors = this.CallInterceptors.Length;
            if (nInterceptors > 0)
            {
                var metadata = new LightweightCallMetadata();
                for (int interceptorIndex = 0; interceptorIndex < nInterceptors; interceptorIndex++)
                {
                    this.CallInterceptors[interceptorIndex](metadata);
                }

                headers = metadata.Headers;
            }

            return headers;
        }

        private class LightweightCallMetadata : IRpcClientCallContext
        {
            private Dictionary<string, string> headersDictionary = new Dictionary<string, string>();

            internal IReadOnlyDictionary<string, string> Headers => this.headersDictionary;

            public void AddHeader(string key, string value)
            {
                this.headersDictionary.Add(key, value);
            }

            public string? GetHeaderString(string key)
            {
                this.headersDictionary.TryGetValue(key, out string? value);
                return value;
            }
        }
    }

#pragma warning restore CA1062 // Validate arguments of public methods

    internal sealed class LightweightSerializers<TRequest, TResponse>
    {
        internal readonly IRpcSerializer<TRequest> RequestSerializer;

        internal readonly IRpcSerializer<TResponse> ResponseSerializer;

        public LightweightSerializers(IRpcSerializer<TRequest> requestSerializer, IRpcSerializer<TResponse> responseSerializer)
        {
            this.RequestSerializer = requestSerializer;
            this.ResponseSerializer = responseSerializer;
        }
    }

    internal class LightweightSerializersCache
    {
        private readonly Dictionary<RpcProxyMethod, object> proxyToSerializers = new Dictionary<RpcProxyMethod, object>();

        private readonly IRpcSerializer serializer;

        private readonly object syncRoot = new object();

        internal LightweightSerializersCache(IRpcSerializer serializer)
        {
            this.serializer = serializer;
        }

        internal LightweightSerializers<TRequest, TResponse> GetSerializers<TRequest, TResponse>(RpcProxyMethod proxyMethod)
            where TRequest : class
            where TResponse : class
        {
            lock (this.syncRoot)
            {
                if (this.proxyToSerializers.TryGetValue(proxyMethod, out var serializers))
                {
                    return (LightweightSerializers<TRequest, TResponse>)serializers;
                }

                var newSerializers = new LightweightSerializers<TRequest, TResponse>(this.serializer.CreateTyped<TRequest>(), this.serializer.CreateTyped<TResponse>());
                this.proxyToSerializers.Add(proxyMethod, newSerializers);

                return newSerializers;
            }
        }
    }
}
