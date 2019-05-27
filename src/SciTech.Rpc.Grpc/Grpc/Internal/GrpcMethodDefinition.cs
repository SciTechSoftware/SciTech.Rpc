using System;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.Grpc.Server.Internal
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
