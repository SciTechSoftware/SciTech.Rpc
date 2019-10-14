using SciTech.Rpc.Client;
using SciTech.Rpc.NetGrpc.Client.Internal;
using System;
using GrpcNet = Grpc.Net;

namespace SciTech.Rpc.NetGrpc.Client
{
    public class NetGrpcConnectionProvider : IRpcConnectionProvider
    {
        private readonly IRpcProxyDefinitionsProvider? definitionsProvider;

        private GrpcNet.Client.GrpcChannelOptions? channelOptions;

        private ImmutableRpcClientOptions? options = null;

        public NetGrpcConnectionProvider(
            ImmutableRpcClientOptions? options = null,
            IRpcProxyDefinitionsProvider? definitionsProvider = null,
            GrpcNet.Client.GrpcChannelOptions? channelOptions = null)
        {
            this.channelOptions = channelOptions;
            this.options = options;
            this.definitionsProvider = definitionsProvider;
        }

        public bool CanCreateConnection(RpcServerConnectionInfo connectionInfo)
        {
            return connectionInfo?.HostUrl?.Scheme == WellKnownRpcSchemes.Grpc;
        }

        public IRpcServerConnection CreateConnection(RpcServerConnectionInfo connectionInfo, ImmutableRpcClientOptions? options, IRpcProxyDefinitionsProvider? definitionsProvider)
        {
            if (connectionInfo?.HostUrl?.Scheme == WellKnownRpcSchemes.Grpc)
            {
                var actualDefinitionsProvider = this.definitionsProvider ?? definitionsProvider;
                var proxyGenerator = GrpcProxyGenerator.Factory.CreateProxyGenerator(actualDefinitionsProvider);

                return new NetGrpcServerConnection(connectionInfo, ImmutableRpcClientOptions.Combine(this.options, options), proxyGenerator, this.channelOptions);
            }

            throw new ArgumentException("Unsupported connection info. Use CanCreateConnection to check whether a connection can be created.");
        }

    }
}
