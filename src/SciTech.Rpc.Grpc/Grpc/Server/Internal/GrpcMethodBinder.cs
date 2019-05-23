using System;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.Grpc.Server.Internal
{
    internal interface IGrpcMethodBinder
    {
        void AddMethod<TRequest, TResponse>(GrpcCore.Method<TRequest, TResponse> method, GrpcCore.UnaryServerMethod<TRequest, TResponse> handler) where TResponse : class where TRequest : class;

        void AddMethod<TRequest, TResponse>(GrpcCore.Method<TRequest, TResponse> method, GrpcCore.ServerStreamingServerMethod<TRequest, TResponse> handler) where TResponse : class where TRequest : class;
    }

    internal class GrpcMethodBinder : IGrpcMethodBinder
    {
        private GrpcCore.ServerServiceDefinition.Builder builder;

        public GrpcMethodBinder(GrpcCore.ServerServiceDefinition.Builder builder)
        {
            this.builder = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        public void AddMethod<TRequest, TResponse>(GrpcCore.Method<TRequest, TResponse> method, GrpcCore.UnaryServerMethod<TRequest, TResponse> handler) where TResponse : class where TRequest : class
        {
            this.builder.AddMethod(method, handler);
        }

        public void AddMethod<TRequest, TResponse>(GrpcCore.Method<TRequest, TResponse> method, GrpcCore.ServerStreamingServerMethod<TRequest, TResponse> handler) where TResponse : class where TRequest : class
        {
            this.builder.AddMethod(method, handler);
        }
    }
}
