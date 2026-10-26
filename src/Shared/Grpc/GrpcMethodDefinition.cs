#region Copyright notice and license
// Copyright (c) SciTech Software AB and contributors.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using SciTech.Rpc.Serialization;
using System;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.NetGrpc.Internal
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
                    deserializer: requestSerializer.Deserialize!
                ),
                responseMarshaller: GrpcCore.Marshallers.Create<TResponse>(
                    serializer: responseSerializer.Serialize,
                    deserializer: responseSerializer.Deserialize!
                )
            );
        }
    }
}
