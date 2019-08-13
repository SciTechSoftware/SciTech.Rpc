using SciTech.Rpc.Client;
using SciTech.Rpc.Lightweight;
using SciTech.Rpc.Server;
using System;

namespace SciTech.Rpc.Tests.Lightweight
{
    public class LightweightConnectionCreator : ITestConnectionCreator
    {
        private LightweightOptions options;

        public LightweightConnectionCreator(RpcConnectionType connectionType, LightweightOptions options = null)
        {
            this.options = options;
        }

        public (IRpcServer, RpcServerConnection) CreateServerAndConnection(
            RpcServiceDefinitionBuilder serviceDefinitionsBuilder,
            Action<RpcServerOptions> configServerOptions = null,
            Action<RpcClientOptions> configClientOptions = null,
            IRpcProxyDefinitionsProvider proxyServicesProvider = null)
        {
            throw new NotImplementedException();
        }
    }
}
