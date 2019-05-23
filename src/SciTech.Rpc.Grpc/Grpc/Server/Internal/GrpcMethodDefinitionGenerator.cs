using System;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.Grpc.Server.Internal
{
    public static class GrpcMethodDefinitionGenerator
    {
        public static GrpcCore.Method<TRequest, TResponse> CreateMethodDefinition<TRequest, TResponse>(
            GrpcCore.MethodType methodType,
            string serviceName,
            string methodName,
            IRpcSerializer serializer
        )
            where TRequest : class
            where TResponse : class
        {
#nullable disable
            return new GrpcCore.Method<TRequest, TResponse>(
                type: methodType,
                serviceName: serviceName,
                name: methodName,
                requestMarshaller: GrpcCore.Marshallers.Create(
                    serializer: serializer.ToBytes,
                    deserializer: serializer.FromBytes<TRequest>
                ),
                responseMarshaller: GrpcCore.Marshallers.Create(
                    serializer: serializer.ToBytes,
                    deserializer: serializer.FromBytes<TResponse>
                )
            );
#nullable restore
        }
    }
}
