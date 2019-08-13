#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB and TA Instrument Inc.
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
using SciTech.Rpc.Grpc.Server.Internal;
using SciTech.Rpc.Internal;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using System.Threading.Tasks;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.Grpc.Client.Internal
{
    internal class GrpcMethodsCache
    {
        private readonly Dictionary<GrpcProxyMethod, GrpcCore.IMethod> proxyToGrpcMethod = new Dictionary<GrpcProxyMethod, GrpcCore.IMethod>();

        private readonly IRpcSerializer serializer;

        private readonly object syncRoot = new object();

        internal GrpcMethodsCache(IRpcSerializer serializer)
        {
            this.serializer = serializer;
        }

        internal GrpcCore.Method<TRequest, TResponse> GetGrpcMethod<TRequest, TResponse>(GrpcProxyMethod proxyMethod)
            where TRequest : class
            where TResponse : class
        {
            lock (this.syncRoot)
            {
                if (this.proxyToGrpcMethod.TryGetValue(proxyMethod, out var grpcMethod))
                {
                    return (GrpcCore.Method<TRequest, TResponse>)grpcMethod;
                }

                var newGrpcMethod = proxyMethod.CreateMethod<TRequest, TResponse>(this.serializer);
                this.proxyToGrpcMethod.Add(proxyMethod, newGrpcMethod);

                return newGrpcMethod;
            }
        }
    }

#pragma warning disable CA1062 // Validate arguments of public methods
    public abstract class GrpcProxyBase : RpcProxyBase<GrpcProxyMethod>
    {
        protected override TResponse CallUnaryMethodImpl<TRequest, TResponse>(GrpcProxyMethod methodDef, TRequest request)
        {
            var callOptions = new GrpcCore.CallOptions(cancellationToken: CancellationToken.None);
            var typedMethod = this.grpcMethodsCache.GetGrpcMethod<TRequest, TResponse>(methodDef);

            var response = this.grpcInvoker.BlockingUnaryCall(typedMethod, null, callOptions, request);
            return response;
        }

        protected async override Task<TResponse> CallUnaryMethodImplAsync<TRequest, TResponse>(GrpcProxyMethod methodDef, TRequest request, CancellationToken cancellationToken)
        {
            var callOptions = new GrpcCore.CallOptions(cancellationToken: cancellationToken);

            var typedMethod = this.grpcMethodsCache.GetGrpcMethod<TRequest, TResponse>(methodDef);
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
            if (e is GrpcCore.RpcException rpcException)
            {
                switch (rpcException.StatusCode)
                {
                    case GrpcCore.StatusCode.Unavailable:
                        throw new RpcCommunicationException(RpcCommunicationStatus.Unavailable, e.Message, e);
                    case GrpcCore.StatusCode.ResourceExhausted:
                        throw new RpcFailureException(RpcFailure.SizeLimitExceeded, e.Message, e);
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
        private sealed class GrpcAsyncServerStreamingCall<TResponse> : IAsyncStreamingServerCall<TResponse>
            where TResponse : class
        {
            private GrpcCore.AsyncServerStreamingCall<TResponse> grpcCall;

            internal GrpcAsyncServerStreamingCall(GrpcCore.AsyncServerStreamingCall<TResponse> grpcCall)
            {
                this.grpcCall = grpcCall;
                this.ResponseStream = new AsyncStreamWrapper(this.grpcCall.ResponseStream);


            }

            public IAsyncStream<TResponse> ResponseStream { get; }

            public void Dispose()
            {
                this.grpcCall.Dispose();
            }

            private class AsyncStreamWrapper : IAsyncStream<TResponse>
            {
                private GrpcCore.IAsyncStreamReader<TResponse> reader;

                public AsyncStreamWrapper(GrpcCore.IAsyncStreamReader<TResponse> reader)
                {
                    this.reader = reader;
                }

                public TResponse Current => this.reader.Current;

                public ValueTask<bool> MoveNextAsync()
                {
                    var task = this.reader.MoveNext();
                    if (task.Status == TaskStatus.RanToCompletion)
                    {
                        return new ValueTask<bool>(task.Result);
                    }

                    async ValueTask<bool> AwaitNext() => await task.ContextFree();

                    return AwaitNext();
                }
            }
        }

        private readonly GrpcCore.CallInvoker grpcInvoker;

        private readonly GrpcMethodsCache grpcMethodsCache;

        protected GrpcProxyBase(GrpcProxyArgs proxyArgs, GrpcProxyMethod[] proxyMethods) : base(proxyArgs, proxyMethods)
        {
            this.grpcInvoker = proxyArgs.CallInvoker;
            this.Serializer = proxyArgs.Serializer;
            this.grpcMethodsCache = proxyArgs.MethodsCache;
        }

        public IRpcSerializer Serializer { get; }

        public static GrpcProxyMethod CreateMethodDef<TRequest, TResponse>(
            RpcMethodType methodType, string serviceName, string methodName,
            IRpcSerializer? serializerOverride,
            RpcClientFaultHandler? faultHandler)
        {
            GrpcCore.MethodType grpcMethodType;
            switch (methodType)
            {
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
            //var grpcMethod = new GrpcCore.Method<TRequest, TResponse>(
            //    type: grpcMethodType,
            //    serviceName: serviceName,
            //    name: methodName,
            //    requestMarshaller: GrpcCore.Marshallers.Create(
            //        serializer: serializer.ToBytes,
            //        deserializer: serializer.FromBytes<TRequest>
            //        ),
            //    responseMarshaller: GrpcCore.Marshallers.Create(
            //        serializer: serializer.ToBytes,
            //        deserializer: serializer.FromBytes<TResponse>
            //        )
            //    );

            return new GrpcProxyMethod(grpcMethodType, serviceName, methodName, serializerOverride, faultHandler);
        }

        protected override ValueTask<IAsyncStreamingServerCall<TResponse>> CallStreamingMethodAsync<TRequest, TResponse>(TRequest request, GrpcProxyMethod method, CancellationToken ct)
        {
            var callOptions = new GrpcCore.CallOptions(cancellationToken: ct);

            var typedMethod = this.grpcMethodsCache.GetGrpcMethod<TRequest, TResponse>(method);

#pragma warning disable CA2000 // Dispose objects before losing scope
            return new ValueTask<IAsyncStreamingServerCall<TResponse>>(
                new GrpcAsyncServerStreamingCall<TResponse>(this.grpcInvoker.AsyncServerStreamingCall(typedMethod, null, callOptions, request)));
#pragma warning restore CA2000 // Dispose objects before losing scope
        }
    }

#pragma warning restore CA1062 // Validate arguments of public methods

    public class GrpcProxyMethod : RpcProxyMethod
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

        public GrpcCore.Method<TRequest, TResponse> CreateMethod<TRequest, TResponse>(IRpcSerializer serializer)
            where TRequest : class
            where TResponse : class
        {
            IRpcSerializer actualSerializer = this.SerializerOverride ?? serializer;
            return GrpcMethodDefinition.Create<TRequest, TResponse>(
                methodType: this.MethodType,
                serviceName: this.ServiceName,
                methodName: this.MethodName,
                actualSerializer);
#nullable restore
        }
    }
}
