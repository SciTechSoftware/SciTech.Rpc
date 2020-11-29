using SciTech.Collections;
using SciTech.Rpc.Client;
using SciTech.Rpc.Grpc.Client.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.Grpc.Client
{
    public class GrpcConnectionProvider : IRpcConnectionProvider
    {
        public const string GrpcScheme = "grpc";

        private readonly IRpcProxyDefinitionsProvider? definitionsProvider;

        private readonly IImmutableList<GrpcCore.ChannelOption>? channelOptions;

        private readonly GrpcCore.ChannelCredentials credentials;

        private readonly ImmutableRpcClientOptions? options;

        public GrpcConnectionProvider(
            IRpcClientOptions? options = null, IRpcProxyDefinitionsProvider? definitionsProvider = null,
            IEnumerable<GrpcCore.ChannelOption>? channelOptions = null)
            : this(GrpcCore.ChannelCredentials.Insecure, options, definitionsProvider, channelOptions )
        {
        }

        public GrpcConnectionProvider(
            GrpcCore.ChannelCredentials credentials,
            IRpcClientOptions? options = null,
            IRpcProxyDefinitionsProvider? definitionsProvider = null,
            IEnumerable<GrpcCore.ChannelOption>? channelOptions = null)
        {
            this.credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            this.options = options?.AsImmutable();
            this.channelOptions = channelOptions?.AsImmutableArrayList();

            this.definitionsProvider = definitionsProvider;
        }

        public bool CanCreateChannel(RpcServerConnectionInfo connectionInfo)
        {
            return connectionInfo?.HostUrl?.Scheme == GrpcScheme;
        }

        public IRpcChannel CreateChannel(RpcServerConnectionInfo connectionInfo, IRpcClientOptions? options, IRpcProxyDefinitionsProvider? definitionsProvider)
        {
            if (connectionInfo?.HostUrl?.Scheme == GrpcScheme)
            {
                var actualDefinitionsProvider = this.definitionsProvider ?? definitionsProvider;
                var proxyGenerator = GrpcProxyGenerator.Factory.Default;

                return new GrpcServerConnection(connectionInfo, this.credentials,
                    ImmutableRpcClientOptions.Combine(options, this.options), proxyGenerator, this.channelOptions);
            }

            throw new ArgumentException("Unsupported connection info. Use CanCreateConnection to check whether a connection can be created.");
        }
    }
}
