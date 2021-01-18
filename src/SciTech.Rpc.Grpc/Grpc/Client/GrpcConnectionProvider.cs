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

        private readonly IImmutableList<GrpcCore.ChannelOption>? channelOptions;

        private readonly GrpcCore.ChannelCredentials credentials;

        private readonly ImmutableRpcClientOptions? options;

        public GrpcConnectionProvider(
            IRpcClientOptions? options = null, 
            IEnumerable<GrpcCore.ChannelOption>? channelOptions = null)
            : this(GrpcCore.ChannelCredentials.Insecure, options, channelOptions )
        {
        }

        public GrpcConnectionProvider(
            GrpcCore.ChannelCredentials credentials,
            IRpcClientOptions? options = null,            
            IEnumerable<GrpcCore.ChannelOption>? channelOptions = null)
        {
            this.credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            this.options = options?.AsImmutable();
            this.channelOptions = channelOptions?.ToImmutableArrayList();
        }

        public bool CanCreateChannel(RpcConnectionInfo connectionInfo)
        {
            return connectionInfo?.HostUrl?.Scheme == GrpcScheme;
        }

        public IRpcChannel CreateChannel(RpcConnectionInfo connectionInfo, IRpcClientOptions? options)
        {
            if (connectionInfo?.HostUrl?.Scheme == GrpcScheme)
            {
                var proxyGenerator = GrpcProxyGenerator.Default;

                return new GrpcConnection(connectionInfo, this.credentials,
                    ImmutableRpcClientOptions.Combine(options, this.options), proxyGenerator, this.channelOptions);
            }

            throw new ArgumentException("Unsupported connection info. Use CanCreateConnection to check whether a connection can be created.");
        }
    }
}
