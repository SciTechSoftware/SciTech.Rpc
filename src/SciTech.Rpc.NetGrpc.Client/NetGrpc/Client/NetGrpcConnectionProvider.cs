using SciTech.Rpc.Client;
using SciTech.Rpc.NetGrpc.Client.Internal;
using System;
using GrpcNet = Grpc.Net;

namespace SciTech.Rpc.NetGrpc.Client
{
    public class NetGrpcConnectionProvider : IRpcConnectionProvider
    {

        public const string GrpcScheme = "grpc";

        private readonly GrpcProxyGenerator proxyGenerator;

        private GrpcNet.Client.GrpcChannelOptions? channelOptions;

        private ImmutableRpcClientOptions? options = null;

        public NetGrpcConnectionProvider(
            ImmutableRpcClientOptions? options = null,
            IRpcProxyDefinitionsProvider? definitionsProvider = null,
            GrpcNet.Client.GrpcChannelOptions? channelOptions = null)
        {
            this.channelOptions = channelOptions;
            this.options = options;
            this.proxyGenerator = GrpcProxyGenerator.Factory.CreateProxyGenerator(definitionsProvider);
        }

        public bool CanCreateConnection(RpcServerConnectionInfo connectionInfo)
        {
            return connectionInfo?.HostUrl?.Scheme == GrpcScheme;
        }

        public IRpcServerConnection CreateConnection(RpcServerConnectionInfo connectionInfo, ImmutableRpcClientOptions? options)
        {
            if (connectionInfo?.HostUrl?.Scheme == GrpcScheme)
            {
                return new NetGrpcServerConnection(connectionInfo, ImmutableRpcClientOptions.Combine(this.options, options), this.proxyGenerator, this.channelOptions);
            }

            throw new ArgumentException("Unsupported connection info. Use CanCreateConnection to check whether a connection can be created.");
        }

    }
}
