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
            SynchronizationContext? syncContext)
            : base(connection, objectId, serializer, implementedServices, syncContext)
        {
            this.MethodSerializersCache = methodSerializersCache;
            this.CallInterceptors = callInterceptors;
        }

        internal IReadOnlyList<RpcClientCallInterceptor> CallInterceptors { get; }

        internal new LightweightRpcConnection Channel => (LightweightRpcConnection)base.Channel;

        internal LightweightSerializersCache MethodSerializersCache { get; }
    }

#pragma warning disable CA1062 // Validate arguments of public methods
    public class LightweightProxyBase : RpcProxyBase
    {
        private readonly int callTimeout;

        private readonly LightweightRpcConnection connection;

        private readonly LightweightSerializersCache methodSerializersCache;

        private readonly int streamingCallTimeout;

        protected LightweightProxyBase(LightweightProxyArgs proxyArgs, LightweightMethodDef[] proxyMethods) 
            : base(proxyArgs, proxyMethods, new LightweightProxyCallDispatcher(proxyArgs))
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




        protected override LightweightMethodDef CreateDynamicMethodDef<TRequest, TResponse>(string serviceName, string operationName)
        {
            return CreateMethodDef<TRequest, TResponse>(RpcMethodType.Unary, serviceName, operationName, this.serializer, null);
        }


        private ValueTask<RpcPipelineClient> ConnectCoreAsync(CancellationToken cancellationToken)
        {
            return this.connection.ConnectClientAsync(cancellationToken);
        }

        private RpcRequestContext? CreateCallHeaders(CancellationToken cancellationToken)
        {
            RpcRequestContext? context = null;
            int nInterceptors = this.CallInterceptors.Length;
            if (nInterceptors > 0 || cancellationToken.CanBeCanceled)
            {
                context = new RpcRequestContext(cancellationToken);
                for (int interceptorIndex = 0; interceptorIndex < nInterceptors; interceptorIndex++)
                {
                    this.CallInterceptors[interceptorIndex](context);
                }
            }

            return context;
        }

        //private class LightweightCallMetadata : IRpcRequestContext
        //{
        //    private Dictionary<string, string> headersDictionary = new Dictionary<string, string>();

        //    internal IReadOnlyDictionary<string, string> Headers => this.headersDictionary;

        //    public void AddHeader(string key, string value)
        //    {
        //        this.headersDictionary.Add(key, value);
        //    }

        //    public string? GetHeaderString(string key)
        //    {
        //        this.headersDictionary.TryGetValue(key, out string? value);
        //        return value;
        //    }
        //}
    }

#pragma warning restore CA1062 // Validate arguments of public methods

    internal sealed class LightweightSerializers<TRequest, TResponse>
    {
        internal readonly IRpcSerializer Serializer;

        internal readonly IRpcSerializer<TRequest> RequestSerializer;

        internal readonly IRpcSerializer<TResponse> ResponseSerializer;

        public LightweightSerializers(IRpcSerializer serializer)
        {
            this.Serializer = serializer;
            this.RequestSerializer = serializer.CreateTyped<TRequest>();
            this.ResponseSerializer = serializer.CreateTyped<TResponse>();
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

                var newSerializers = new LightweightSerializers<TRequest, TResponse>(this.serializer);
                this.proxyToSerializers.Add(proxyMethod, newSerializers);

                return newSerializers;
            }
        }
    }


    internal class LightweightProxyCallDispatcher : IProxyCallDispatcher
    {
        private readonly LightweightSerializersCache methodSerializersCache;
        private LightweightRpcConnection connection;
        private int callTimeout;
        private readonly int streamingCallTimeout;

        internal LightweightProxyCallDispatcher(LightweightProxyArgs proxyArgs)
        {
            this.connection = proxyArgs.Channel;
            this.callTimeout = ((int?)this.connection.Options.CallTimeout?.TotalMilliseconds) ?? 0;
            this.streamingCallTimeout = ((int?)this.connection.Options.StreamingCallTimeout?.TotalMilliseconds) ?? 0;
            this.methodSerializersCache = proxyArgs.MethodSerializersCache;
            this.CallInterceptors = proxyArgs.CallInterceptors.ToImmutableArray();
            this.serializer = proxyArgs.Serializer;
            this.Channel = proxyArgs.Channel;
        }

        private ImmutableArray<RpcClientCallInterceptor> CallInterceptors { get; }

        private IRpcSerializer serializer;

        protected LightweightRpcConnection Channel { get; }

        public ValueTask<IAsyncStreamingServerCall<TResponse>> CallStreamingMethodAsync<TRequest, TResponse>(TRequest request, RpcProxyMethod method, CancellationToken cancellationToken)
            where TRequest : class
            where TResponse : class
        {
            var context = this.CreateCallHeaders(cancellationToken);

            var actualSerializers = ((LightweightMethodDef<TRequest, TResponse>)method).LightweightSerializersOverride
                ?? this.methodSerializersCache.GetSerializers<TRequest, TResponse>(method);

            var clientTask = this.connection.ConnectClientAsync(cancellationToken);
            if (clientTask.IsCompletedSuccessfully)
            {
                var client = clientTask.Result;
                var streamingCall = client.BeginStreamingServerCall(
                    RpcFrameType.StreamingRequest,
                    ((LightweightMethodDef)method).OperationName,
                    context,
                    request,
                    actualSerializers,
                    this.streamingCallTimeout);

                return new ValueTask<IAsyncStreamingServerCall<TResponse>>(streamingCall);
            }

            async ValueTask<IAsyncStreamingServerCall<TResponse>> AwaitConnectAndCall(ValueTask<RpcPipelineClient> pendingClientTask)
            {
                var client = await pendingClientTask.ContextFree();
                var streamingCall = client.BeginStreamingServerCall(
                    RpcFrameType.StreamingRequest,
                    ((LightweightMethodDef)method).OperationName,
                    context,
                    request,
                    actualSerializers,
                    this.streamingCallTimeout);

                return streamingCall;
            }

            return AwaitConnectAndCall(clientTask);
        }



        public TResponse CallUnaryMethodCore<TRequest, TResponse>(RpcProxyMethod methodDef, TRequest request, CancellationToken cancellationToken)
            where TRequest : class
            where TResponse : class
        {
            return this.CallUnaryMethodCoreAsync<TRequest, TResponse>(methodDef, request, cancellationToken).AwaiterResult();
        }

        public Task<TResponse> CallUnaryMethodCoreAsync<TRequest, TResponse>(RpcProxyMethod methodDef, TRequest request, CancellationToken cancellationToken)
            where TRequest : class
            where TResponse : class
        {
            var context = this.CreateCallHeaders(cancellationToken);

            var actualSerializers = ((LightweightMethodDef<TRequest, TResponse>)methodDef).LightweightSerializersOverride
                ?? this.methodSerializersCache.GetSerializers<TRequest, TResponse>(methodDef);

            var clientTask = this.connection.ConnectClientAsync(cancellationToken);
            if (clientTask.IsCompletedSuccessfully)
            {
                var client = clientTask.Result;
                var responseTask = client.SendReceiveFrameAsync<TRequest, TResponse>(
                    RpcFrameType.UnaryRequest,
                    ((LightweightMethodDef)methodDef).OperationName,
                    context,
                    request,
                    actualSerializers,
                    this.callTimeout);

                return responseTask;
            }

            async Task<TResponse> AwaitConnectAndCall(ValueTask<RpcPipelineClient> pendingClientTask)
            {
                var client = await pendingClientTask.ContextFree();
                return await client.SendReceiveFrameAsync<TRequest, TResponse>(
                    RpcFrameType.UnaryRequest,
                    ((LightweightMethodDef)methodDef).OperationName,
                    context,
                    request,
                    actualSerializers,
                    this.callTimeout).ContextFree();
            }

            return AwaitConnectAndCall(clientTask);
        }

        public void HandleCallException(RpcProxyMethod methodDef, Exception e)
        {
            switch (e)
            {
                //case Pipelines.Sockets.Unofficial.ConnectionResetException _:
                //    throw new RpcCommunicationException(RpcCommunicationStatus.ConnectionLost, e.Message, e);
                //case Pipelines.Sockets.Unofficial.ConnectionAbortedException _:
                //    throw new RpcCommunicationException(RpcCommunicationStatus.Unavailable, e.Message, e);
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
                case RpcErrorException fre:
                    this.HandleRpcError(methodDef, fre.Error);
                    break;
                default:
                    throw new RpcFailureException(RpcFailure.Unknown, $"Unexepected exception when calling RPC method. {e.Message}", e);
            }
        }

        public RpcProxyMethod CreateDynamicMethodDef<TRequest, TResponse>(string serviceName, string operationName)
        {
            return new LightweightMethodDef<TRequest, TResponse>(RpcMethodType.Unary, $"{serviceName}.{operationName}", this.serializer, null);
        }

        protected void HandleRpcError(RpcProxyMethod methodDef, RpcError error)
        {
            if (methodDef is null) throw new ArgumentNullException(nameof(methodDef));
            if (error is null) throw new ArgumentNullException(nameof(error));

            string message = error.Message ?? $"Error occured in server handler of 'xxx'";// {methodDef.Operation}'";
            switch (error.ErrorType)
            {
                case WellKnownRpcErrors.ServiceUnavailable:
                    throw new RpcServiceUnavailableException(message);
                case WellKnownRpcErrors.Failure:
                    throw new RpcFailureException(RpcFailureException.GetFailureFromFaultCode(error.ErrorCode), message);
                case WellKnownRpcErrors.Fault:
                    // Just leave switch and handle fault below.
                    break;
                default:
                    throw new RpcFailureException(RpcFailure.Unknown, $"Operation returned an unknown error of type '{error.ErrorType}'. {message}");
            }

            var actualSerializer = methodDef.SerializerOverride ?? this.serializer;
            var faultException = methodDef.FaultHandler.CreateFaultException(error, actualSerializer);
            var convertedException = methodDef.FaultHandler.TryConvertException(faultException);
            if (convertedException == null)
            {
                // Could not be converted by a declared converter, but maybe a registered converter can.
                // TODO: Currently there's no support for service level exception converters (as exists on the server side).
                foreach (var converter in this.Channel.Options.ExceptionConverters)
                {
                    convertedException = converter.TryCreateException(faultException);
                    if (convertedException != null)
                    {
                        break;
                    }
                }
            }

            throw convertedException ?? faultException;
        }


        private RpcRequestContext? CreateCallHeaders(CancellationToken cancellationToken)
        {
            RpcRequestContext? context = null;
            int nInterceptors = this.CallInterceptors.Length;
            if (nInterceptors > 0 || cancellationToken.CanBeCanceled)
            {
                context = new RpcRequestContext(cancellationToken);
                for (int interceptorIndex = 0; interceptorIndex < nInterceptors; interceptorIndex++)
                {
                    this.CallInterceptors[interceptorIndex](context);
                }
            }

            return context;
        }
    }
}
