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
        public LightweightProxyArgs(
            LightweightRpcConnection connection,
            IReadOnlyList<RpcClientCallInterceptor> callInterceptors,
            RpcObjectId objectId,
            IRpcSerializer serializer,
            IReadOnlyCollection<string>? implementedServices,
            IRpcProxyDefinitionsProvider proxyServicesProvider,
            SynchronizationContext? syncContext)
            : base(connection, objectId, serializer, implementedServices, proxyServicesProvider, syncContext)
        {
            this.CallInterceptors = callInterceptors;
        }

        public IReadOnlyList<RpcClientCallInterceptor> CallInterceptors { get; }

        public new LightweightRpcConnection Connection => (LightweightRpcConnection)base.Connection;
    }

#pragma warning disable CA1062 // Validate arguments of public methods
    public class LightweightProxyBase : RpcProxyBase<LightweightMethodDef>
    {
        private readonly int callTimeout;
        
        private readonly int streamingCallTimeout;

        private readonly LightweightRpcConnection connection;

        protected LightweightProxyBase(LightweightProxyArgs proxyArgs, LightweightMethodDef[] proxyMethods) : base(proxyArgs, proxyMethods)
        {
            this.connection = proxyArgs.Connection;
            this.callTimeout = ((int?)this.connection.Options.CallTimeout?.TotalMilliseconds) ?? 0;
            this.streamingCallTimeout = ((int?)this.connection.Options.StreamingCallTimeout?.TotalMilliseconds) ?? 0;
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
            return new LightweightMethodDef(methodType, $"{serviceName}.{methodName}", typeof(TRequest), typeof(TResponse), serializer, faultHandler);
        }

        public Task ConnectAsync()
        {
            return this.ConnectCoreAsync().AsTask();
        }

        protected override ValueTask<IAsyncStreamingServerCall<TResponse>> CallStreamingMethodAsync<TRequest, TResponse>(
            TRequest request, LightweightMethodDef method, CancellationToken ct)
        {
            IReadOnlyDictionary<string, string>? headers = this.CreateCallHeaders();
            var actualSerializer = method.SerializerOverride ?? this.serializer;

            var clientTask = this.ConnectCoreAsync();
            if (clientTask.IsCompletedSuccessfully)
            {
                var client = clientTask.Result;
                var streamingCallTask = client.BeginStreamingServerCall<TRequest, TResponse>(
                    RpcFrameType.StreamingRequest,
                    method.OperationName,
                    headers,
                    request,
                    actualSerializer,
                    this.streamingCallTimeout,
                    ct);

                return streamingCallTask;
            }

            async ValueTask<IAsyncStreamingServerCall<TResponse>> AwaitConnectAndCall(ValueTask<RpcPipelineClient> pendingClientTask)
            {
                var client = await pendingClientTask.ContextFree();
                var streamingCall = await client.BeginStreamingServerCall<TRequest, TResponse>(
                    RpcFrameType.StreamingRequest,
                    method.OperationName,
                    headers,
                    request,
                    actualSerializer,
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
            var actualSerializer = methodDef.SerializerOverride ?? this.serializer;

            var clientTask = this.ConnectCoreAsync();
            if (clientTask.IsCompletedSuccessfully)
            {
                var client = clientTask.Result;
                var responseTask = client.SendReceiveFrameAsync2<TRequest, TResponse>(
                    RpcFrameType.UnaryRequest,
                    methodDef.OperationName,
                    headers,
                    request,
                    actualSerializer,
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
                    actualSerializer,
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

        private ValueTask<RpcPipelineClient> ConnectCoreAsync()
        {
            return this.connection.ConnectClientAsync();
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

        private class LightweightCallMetadata : IRpcClientCallMetadata
        {
            private Dictionary<string, string> headersDictionary = new Dictionary<string, string>();

            internal IReadOnlyDictionary<string, string> Headers => this.headersDictionary;

            public void AddValue(string key, string value)
            {
                this.headersDictionary.Add(key, value);
            }
        }
    }
#pragma warning restore CA1062 // Validate arguments of public methods
}
