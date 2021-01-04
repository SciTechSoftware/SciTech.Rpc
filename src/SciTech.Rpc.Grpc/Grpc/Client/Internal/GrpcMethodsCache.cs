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

using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Serialization;
using System;
using System.Collections.Generic;
using GrpcCore = Grpc.Core;

#if FEATURE_NET_GRPC
namespace SciTech.Rpc.NetGrpc.Client.Internal
#else
namespace SciTech.Rpc.Grpc.Client.Internal
#endif
{
    internal class GrpcMethodsCache
    {
        private readonly Dictionary<RpcProxyMethod, GrpcCore.IMethod> proxyToGrpcMethod = new Dictionary<RpcProxyMethod, GrpcCore.IMethod>();

        private readonly IRpcSerializer serializer;

        private readonly object syncRoot = new object();

        internal GrpcMethodsCache(IRpcSerializer serializer)
        {
            this.serializer = serializer;
        }

        internal GrpcCore.Method<TRequest, TResponse> GetGrpcMethod<TRequest, TResponse>(RpcProxyMethod proxyMethod)
            where TRequest : class
            where TResponse : class
        {
            lock (this.syncRoot)
            {
                if (this.proxyToGrpcMethod.TryGetValue(proxyMethod, out var grpcMethod))
                {
                    return (GrpcCore.Method<TRequest, TResponse>)grpcMethod;
                }

                var newGrpcMethod = ((GrpcProxyMethod<TRequest, TResponse>)proxyMethod).CreateMethod(this.serializer);
                this.proxyToGrpcMethod.Add(proxyMethod, newGrpcMethod);

                return newGrpcMethod;
            }
        }
    }
}
