using SciTech.Rpc.Client;
using System;
using GrpcNet = Grpc.Net;

namespace SciTech.Rpc.NetGrpc.Client
{
    public class NetGrpcConnectionProvider : IRpcConnectionProvider
    {

        public const string GrpcScheme = "grpc";

        private readonly NetGrpcProxyProvider proxyProvider;

        private GrpcNet.Client.GrpcChannelOptions? channelOptions;

        private ImmutableRpcClientOptions? options = null;

        public NetGrpcConnectionProvider(
            ImmutableRpcClientOptions? options = null,
            GrpcNet.Client.GrpcChannelOptions? channelOptions = null,
            NetGrpcProxyProvider? proxyGenerator = null)
        {
            this.channelOptions = channelOptions;
            this.options = options;
            this.proxyProvider = proxyGenerator ?? NetGrpcProxyProvider.Default;
        }

        public bool CanCreateConnection(RpcServerConnectionInfo connectionInfo)
        {
            if (Uri.TryCreate(connectionInfo?.HostUrl, UriKind.Absolute, out var parsedUrl))
            {
                if (parsedUrl.Scheme == GrpcScheme)
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
                if (parsedUrl.Scheme == GrpcScheme)
                {
                    return new NetGrpcServerConnection(connectionInfo, options, this.channelOptions, this.proxyProvider);
                }
            }


            throw new ArgumentException("Unsupported connection info. Use CanCreateConnection to check whether a connection can be created.");
        }

    }
}
