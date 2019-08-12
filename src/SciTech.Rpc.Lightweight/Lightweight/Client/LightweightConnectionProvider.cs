using SciTech.Rpc.Client;
using System;

namespace SciTech.Rpc.Lightweight.Client
{
    public class LightweightConnectionProvider : IRpcConnectionProvider
    {
        public const string LightweightTcpScheme = "lightweight.tcp";

        private readonly ImmutableRpcClientOptions? options;

        private readonly LightweightProxyProvider proxyProvider;

        private readonly SslClientOptions? sslOptions;

        public LightweightConnectionProvider(ImmutableRpcClientOptions? options = null, LightweightProxyProvider? proxyProvider = null)
        {
            this.proxyProvider = proxyProvider ?? LightweightProxyProvider.Default;
            this.options = options;

        }

        public LightweightConnectionProvider(SslClientOptions sslOptions, ImmutableRpcClientOptions? options = null, LightweightProxyProvider ? proxyProvider = null)
        {
            this.sslOptions = sslOptions;
            this.proxyProvider = proxyProvider ?? LightweightProxyProvider.Default;
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

        public IRpcServerConnection CreateConnection(RpcServerConnectionInfo connectionInfo, ImmutableRpcClientOptions? options)
        {
            if (connectionInfo != null && Uri.TryCreate(connectionInfo.HostUrl, UriKind.Absolute, out var parsedUrl))
            {
                if (parsedUrl.Scheme == LightweightTcpScheme)
                {
                    return new TcpLightweightRpcConnection(connectionInfo, this.sslOptions, 
                        ImmutableRpcClientOptions.Combine( options, this.options ), 
                        this.proxyProvider);
                }
            }

            throw new ArgumentException("Unsupported connection info. Use CanCreateConnection to check whether a connection can be created.");
        }
    }
}
