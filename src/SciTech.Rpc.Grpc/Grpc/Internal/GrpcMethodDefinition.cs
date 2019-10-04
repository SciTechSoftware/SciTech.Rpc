using SciTech.Rpc.Serialization;
using System;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.Grpc.Internal
{
    internal static class GrpcMethodDefinition
    {
        public static GrpcCore.Method<TRequest, TResponse> Create<TRequest, TResponse>(
            GrpcCore.MethodType methodType,
            string serviceName,
            string methodName,
            IRpcSerializer serializer
        )
        {
            var requestSerializer = serializer.CreateTyped<TRequest>();
            var responseSerializer = serializer.CreateTyped<TResponse>();
            return new GrpcCore.Method<TRequest, TResponse>(
                type: methodType,
                serviceName: serviceName,
                name: methodName,
                requestMarshaller: GrpcCore.Marshallers.Create<TRequest>(
                    serializer: requestSerializer.Serialize,
                    deserializer: requestSerializer.Deserialize
                ),
                responseMarshaller: GrpcCore.Marshallers.Create<TResponse>(
                    serializer: responseSerializer.Serialize,
                    deserializer: responseSerializer.Deserialize
                )
            );
        }
    }
}
