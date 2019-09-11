using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Lightweight.Client;
using SciTech.Rpc.Lightweight.Client.Internal;
using System;
using System.Linq;

namespace SciTech.Rpc.Tests.Lightweight
{
    public class LightweightProxyTestAdapter : IProxyTestAdapter<LightweightMethodDef>
    {
        public RpcProxyBase<LightweightMethodDef> CreateProxy<TService>() where TService : class
        {
            var generator = new LightweightProxyGenerator();

            var factory = generator.GenerateObjectProxyFactory<TService>(null);
            var proxy = (LightweightProxyBase)factory(RpcObjectId.Empty, new TcpLightweightRpcConnection(new RpcServerConnectionInfo(RpcServerId.Empty)), null);
            return proxy;
        }

        public LightweightMethodDef GetProxyMethod(RpcProxyBase<LightweightMethodDef> proxy, string methodName)
        {
            return proxy.proxyMethods.SingleOrDefault(m => m.OperationName == methodName);
        }

        public Type GetRequestType(LightweightMethodDef method)
        {
            return method.RequestType;
        }

        public Type GetResponseType(LightweightMethodDef method)
        {
            return method.ResponseType;
        }
    }
}
