using SciTech.Rpc.Client;
using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Text;

namespace SciTech.Rpc.Pipelines.Client
{
    public class PipelinesConnectionProvider : IRpcConnectionProvider
    {
        public const string PipelinesTcpScheme = "pipelines.tcp";

        private readonly PipelinesProxyProvider proxyProvider;

        private readonly IRpcSerializer serializer;

        public PipelinesConnectionProvider(PipelinesProxyProvider? proxyProvider = null, IRpcSerializer? serializer = null)
        {
            this.proxyProvider = proxyProvider ?? new PipelinesProxyProvider();
            this.serializer = serializer ?? new ProtobufSerializer();
        }

        public bool CanCreateConnection(RpcServerConnectionInfo connectionInfo)
        {
            if (Uri.TryCreate(connectionInfo?.HostUrl, UriKind.Absolute, out var parsedUrl))
            {
                if (parsedUrl.Scheme == PipelinesTcpScheme)
                {
                    return true;
                }
            }

            return false;
        }

        public IRpcServerConnection CreateConnection(RpcServerConnectionInfo connectionInfo, IReadOnlyList<RpcClientCallInterceptor> callInterceptors)
        {
            if (connectionInfo != null && Uri.TryCreate(connectionInfo.HostUrl, UriKind.Absolute, out var parsedUrl))
            {
                if (parsedUrl.Scheme == PipelinesTcpScheme)
                {
                    return new TcpPipelinesConnection(connectionInfo, this.proxyProvider, this.serializer, callInterceptors);
                }
            }

            throw new ArgumentException("Unsupported connection info. Use CanCreateConnection to check whether a connection can be created.");
        }
    }
}
