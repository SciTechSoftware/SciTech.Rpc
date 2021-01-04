using SciTech.Rpc.Grpc.Server.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.Tests.Grpc
{
    internal class TestGrpcMethodBinder : IGrpcMethodBinder
    {
        internal List<TestGrpcMethodStub> stubs = new List<TestGrpcMethodStub>();


        public void AddMethod<TRequest, TResponse>(GrpcCore.Method<TRequest, TResponse> method, GrpcCore.UnaryServerMethod<TRequest, TResponse> handler) where TResponse : class where TRequest : class
        {
            this.stubs.Add(new TestGrpcMethodStub(method, typeof(TRequest), typeof(TResponse), null));
        }

        public void AddMethod<TRequest, TResponse>(GrpcCore.Method<TRequest, TResponse> method, GrpcCore.ServerStreamingServerMethod<TRequest, TResponse> handler) where TResponse : class where TRequest : class
        {
            this.stubs.Add(new TestGrpcMethodStub(method, typeof(TRequest), typeof(TResponse), null));
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
