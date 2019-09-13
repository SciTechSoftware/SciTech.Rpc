#if NETCOREAPP3_0
using Grpc.AspNetCore.Server.Model;
using SciTech.Rpc.NetGrpc.Server.Internal;
using SciTech.Rpc.Tests.Grpc;
using System;
using System.Collections.Generic;
using System.Linq;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.Tests.NetGrpc
{
    internal class TestNetGrpcMethodBinder<TService> : INetGrpcBinder<TService> where TService : class
    {
        internal List<TestGrpcMethodStub> stubs = new List<TestGrpcMethodStub>();


        public void AddServerStreamingMethod<TRequest, TResponse>(GrpcCore.Method<TRequest, TResponse> method, ServerStreamingServerMethod<NetGrpcServiceActivator<TService>, TRequest, TResponse> handler)
            where TRequest : class
            where TResponse : class
        {
            this.stubs.Add(new TestGrpcMethodStub(method, typeof(TRequest), typeof(TResponse)));
        }

        public void AddUnaryMethod<TRequest, TResponse>(GrpcCore.Method<TRequest, TResponse> method, UnaryServerMethod<NetGrpcServiceActivator<TService>, TRequest, TResponse> handler)
            where TRequest : class
            where TResponse : class
        {
            this.stubs.Add(new TestGrpcMethodStub(method, typeof(TRequest), typeof(TResponse)));
        }
    }
}
#endif