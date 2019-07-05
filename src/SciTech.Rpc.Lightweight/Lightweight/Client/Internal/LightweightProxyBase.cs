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
        private RpcPipelineClient? client;

        private LightweightRpcConnection connection;

        private TaskCompletionSource<RpcPipelineClient>? connectTcs;

        protected LightweightProxyBase(LightweightProxyArgs proxyArgs, LightweightMethodDef[] proxyMethods) : base(proxyArgs, proxyMethods)
        {
            this.connection = proxyArgs.Connection;

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
                    ct ).ContextFree();

                return streamingCall;
            }

            return AwaitConnectAndCall(clientTask);
        }

        protected override TResponse CallUnaryMethodImpl<TRequest, TResponse>(LightweightMethodDef methodDef, TRequest request)
        {
            return CallUnaryMethodImplAsync<TRequest, TResponse>(methodDef, request, CancellationToken.None).AwaiterResult();

        }

        protected override Task<TResponse> CallUnaryMethodImplAsync<TRequest, TResponse>(LightweightMethodDef methodDef, TRequest request, CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<string, string>? headers = this.CreateCallHeaders();
            var actualSerializer = methodDef.SerializerOverride ?? this.serializer;

            var clientTask = this.ConnectCoreAsync();
            if (clientTask.IsCompletedSuccessfully)
            {
                var client = clientTask.Result;
                var responseTask = client.SendReceiveFrameAsync<TRequest, TResponse>(
                    RpcFrameType.UnaryRequest,
                    methodDef.OperationName,
                    headers,
                    request,
                    actualSerializer,
                    cancellationToken);

                return responseTask;
            }

            async Task<TResponse> AwaitConnectAndCall(ValueTask<RpcPipelineClient> pendingClientTask)
            {
                var client = await pendingClientTask.ContextFree();
                return await client.SendReceiveFrameAsync<TRequest, TResponse>(
                    RpcFrameType.UnaryRequest,
                    methodDef.OperationName,
                    headers,
                    request,
                    actualSerializer, 
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
            if (!(e is RpcFailureException))
            {
                if (e is SocketException socketException)
                {
                    switch (socketException.SocketErrorCode)
                    {
                        case SocketError.ConnectionAborted:
                        case SocketError.ConnectionRefused:
                        case SocketError.ConnectionReset:
                        case SocketError.HostDown:
                        case SocketError.HostNotFound:
                        case SocketError.HostUnreachable:
                            throw new RpcCommunicationException(RpcCommunicationStatus.Unavailable);
                        default:
                            throw new RpcCommunicationException(RpcCommunicationStatus.Unknown);
                    }
                }

                throw new RpcFailureException("Unexepected exception when calling RPC method", e);
            }
        }

        protected override bool IsCommunicationException(Exception exception)
        {
            throw new NotImplementedException();
        }

        private ValueTask<RpcPipelineClient> ConnectCoreAsync()
        {
            Task<RpcPipelineClient>? currentConnectTask = null;
            lock (this.SyncRoot)
            {
                if (this.client != null)
                {
                    return new ValueTask<RpcPipelineClient>(this.client);
                }

                if (this.connectTcs != null)
                {
                    currentConnectTask = this.connectTcs.Task;
                }
                else
                {
                    this.connectTcs = new TaskCompletionSource<RpcPipelineClient>();
                }
            }


            if (currentConnectTask != null)
            {
                async ValueTask<RpcPipelineClient> AwaitConnectTask(Task<RpcPipelineClient> task)
                {
                    return await task.ContextFree();
                }

                return AwaitConnectTask(currentConnectTask);
            }
            else
            {
                async ValueTask<RpcPipelineClient> AwaitConnection()
                {
                    var connectedClient = await this.connection.ConnectClientAsync().ContextFree();

                    var connectTcs = this.connectTcs!;
                    lock (this.SyncRoot)
                    {
                        this.client = connectedClient;
                        this.connectTcs = null;
                    }

                    connectTcs.SetResult(connectedClient);

                    return connectedClient;
                }

                return AwaitConnection();
            }

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
