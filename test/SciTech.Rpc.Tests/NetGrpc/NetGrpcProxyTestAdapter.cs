#if PLAT_NET_GRPC
using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.NetGrpc.Client;
using SciTech.Rpc.NetGrpc.Client.Internal;
using System;
using System.Linq;

namespace SciTech.Rpc.Tests.NetGrpc
{
    public class NetGrpcProxyTestAdapter : IProxyTestAdapter<GrpcProxyMethod>
    {
        public RpcProxyBase<GrpcProxyMethod> CreateProxy<TService>() where TService : class
        {
            var generator = new GrpcProxyGenerator(null);

            var factory = generator.GenerateObjectProxyFactory<TService>(null);
            var proxy = (RpcProxyBase<GrpcProxyMethod>)factory(RpcObjectId.Empty, new NetGrpcServerConnection(new RpcServerConnectionInfo(new Uri("grpc://localhost"))), null);
            return proxy;
        }

        public GrpcProxyMethod GetProxyMethod(RpcProxyBase<GrpcProxyMethod> proxy, string methodName)
        {
            return proxy.proxyMethods.SingleOrDefault(m => $"{m.ServiceName}.{m.MethodName}" == methodName);
        }

    }
}
#endif