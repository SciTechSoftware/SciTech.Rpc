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
            ImmutableRpcClientOptions? options = null, IRpcProxyDefinitionsProvider? definitionsProvider = null,
            IEnumerable<GrpcCore.ChannelOption>? channelOptions = null)
            : this(GrpcCore.ChannelCredentials.Insecure, options, definitionsProvider, channelOptions )
        {
        }

        public GrpcConnectionProvider(
            GrpcCore.ChannelCredentials credentials,
            ImmutableRpcClientOptions? options = null,
            IRpcProxyDefinitionsProvider? definitionsProvider = null,
            IEnumerable<GrpcCore.ChannelOption>? channelOptions = null)
        {
            this.credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            this.options = options;
            this.channelOptions = channelOptions?.AsImmutableArrayList();

            this.definitionsProvider = definitionsProvider;
        }

        public bool CanCreateConnection(RpcServerConnectionInfo connectionInfo)
        {
            return connectionInfo?.HostUrl?.Scheme == GrpcScheme;
        }

        public IRpcServerConnection CreateConnection(RpcServerConnectionInfo connectionInfo, ImmutableRpcClientOptions? options, IRpcProxyDefinitionsProvider? definitionsProvider)
        {
            if (connectionInfo?.HostUrl?.Scheme == GrpcScheme)
            {
                var actualDefinitionsProvider = this.definitionsProvider ?? definitionsProvider;
                var proxyGenerator = GrpcProxyGenerator.Factory.CreateProxyGenerator(actualDefinitionsProvider);

                return new GrpcServerConnection(connectionInfo, this.credentials,
                    ImmutableRpcClientOptions.Combine(options, this.options), proxyGenerator, this.channelOptions);
            }

            throw new ArgumentException("Unsupported connection info. Use CanCreateConnection to check whether a connection can be created.");
        }
    }
}
