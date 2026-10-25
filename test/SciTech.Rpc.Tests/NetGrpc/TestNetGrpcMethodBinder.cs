#if NET
using Grpc.AspNetCore.Server.Model;
using SciTech.Rpc.NetGrpc.Server.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.Tests.NetGrpc
{
    internal class TestNetGrpcMethodBinder<TService> : INetGrpcBinder<TService> where TService : class
    {
        internal List<TestGrpcMethodStub> stubs = new List<TestGrpcMethodStub>();


        public void AddServerStreamingMethod<TRequest, TResponse>(
            GrpcCore.Method<TRequest, TResponse> method,
            ImmutableArray<object> metadata,
            ServerStreamingServerMethod<NetGrpcServiceActivator<TService>, TRequest, TResponse> handler)
            where TRequest : class
            where TResponse : class
        {
            this.stubs.Add(new TestGrpcMethodStub(method, typeof(TRequest), typeof(TResponse), metadata));
        }

        public void AddUnaryMethod<TRequest, TResponse>(
            GrpcCore.Method<TRequest, TResponse> method,
            ImmutableArray<object> metadata,
            UnaryServerMethod<NetGrpcServiceActivator<TService>, TRequest, TResponse> handler)
            where TRequest : class
            where TResponse : class
        {
            this.stubs.Add(new TestGrpcMethodStub(method, typeof(TRequest), typeof(TResponse),metadata));
        }
    }

    internal class TestGrpcMethodStub
    {
        public TestGrpcMethodStub(GrpcCore.IMethod method, Type requestType, Type responseType, IList<object> metadata)
        {
            this.Method = method;
            this.RequestType = requestType;
            this.ResponseType = responseType;
            this.Metadata = metadata;
        }

        internal GrpcCore.IMethod Method { get; }

        internal Type RequestType { get; }

        internal Type ResponseType { get; }

        internal IList<object> Metadata { get; }
    }
}
#endif