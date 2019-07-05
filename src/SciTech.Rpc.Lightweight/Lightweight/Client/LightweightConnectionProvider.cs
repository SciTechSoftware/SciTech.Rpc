using SciTech.Rpc.Client;
using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Text;

namespace SciTech.Rpc.Lightweight.Client
{
    public class LightweightConnectionProvider : IRpcConnectionProvider
    {
        public const string LightweightTcpScheme = "lightweight.tcp";

        private readonly LightweightProxyProvider proxyProvider;

        private readonly IRpcSerializer serializer;

        private readonly SslClientOptions? sslOptions;

        public LightweightConnectionProvider(LightweightProxyProvider? proxyProvider = null, IRpcSerializer? serializer = null)
        {
            this.proxyProvider = proxyProvider ?? new LightweightProxyProvider();
            this.serializer = serializer ?? new ProtobufSerializer();
        }
        public LightweightConnectionProvider(SslClientOptions sslOptions, LightweightProxyProvider? proxyProvider = null, IRpcSerializer? serializer = null)
        {
            this.sslOptions = sslOptions;
            this.proxyProvider = proxyProvider ?? new LightweightProxyProvider();
            this.serializer = serializer ?? new ProtobufSerializer();
        }

        public bool CanCreateConnection(RpcServerConnectionInfo connectionInfo)
        {
            if (Uri.TryCreate(connectionInfo?.HostUrl, UriKind.Absolute, out var parsedUrl))
            {
                if (parsedUrl.Scheme == LightweightTcpScheme)
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
                if (parsedUrl.Scheme == LightweightTcpScheme)
                {
                    return new TcpLightweightRpcConnection(connectionInfo, this.proxyProvider, this.serializer, this.sslOptions, callInterceptors );
                }
            }

            throw new ArgumentException("Unsupported connection info. Use CanCreateConnection to check whether a connection can be created.");
        }
    }
}
