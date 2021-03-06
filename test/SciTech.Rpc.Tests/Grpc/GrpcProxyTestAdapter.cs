﻿using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Grpc.Client;
using SciTech.Rpc.Grpc.Client.Internal;
using System;
using System.Linq;

namespace SciTech.Rpc.Tests.Grpc
{
    public class GrpcProxyTestAdapter : IProxyTestAdapter<GrpcProxyMethod>
    {
        public RpcProxyBase<GrpcProxyMethod> CreateProxy<TService>() where TService : class
        {
            var generator = new GrpcProxyGenerator();

            var factory = generator.GenerateObjectProxyFactory<TService>(null, null);
            var proxy = (RpcProxyBase<GrpcProxyMethod>)factory(RpcObjectId.Empty, new GrpcConnection(new RpcConnectionInfo(new Uri("grpc://localhost"))), null);
            return proxy;
        }

        public GrpcProxyMethod GetProxyMethod(RpcProxyBase<GrpcProxyMethod> proxy, string methodName)
        {
            return proxy.proxyMethods.SingleOrDefault(m => $"{m.ServiceName}.{m.MethodName}" == methodName);
        }

    }
}
