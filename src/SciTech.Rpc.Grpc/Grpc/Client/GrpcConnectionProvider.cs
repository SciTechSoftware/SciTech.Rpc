using SciTech.Collections;
using SciTech.Rpc.Client;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.Grpc.Client
{
    public class GrpcConnectionProvider : IRpcConnectionProvider
    {
        public const string GrpcScheme = "grpc";

        private readonly GrpcProxyProvider proxyProvider;

        private readonly IRpcSerializer serializer;

        private readonly GrpcCore.ChannelCredentials credentials;

        IImmutableList<GrpcCore.ChannelOption>? channelOptions;

        public GrpcConnectionProvider(GrpcProxyProvider? proxyGenerator = null, IRpcSerializer? serializer = null) 
            : this( GrpcCore.ChannelCredentials.Insecure, null, proxyGenerator, serializer )
        {
        }

        public GrpcConnectionProvider(GrpcCore.ChannelCredentials credentials, IEnumerable<GrpcCore.ChannelOption>? channelOptions=null,
            GrpcProxyProvider? proxyGenerator = null, IRpcSerializer? serializer = null)
        {
            this.credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            this.channelOptions = channelOptions?.AsImmutableArrayList();

            this.proxyProvider = proxyGenerator ?? new GrpcProxyProvider();
            this.serializer = serializer ?? new ProtobufSerializer();
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

        public IRpcServerConnection CreateConnection(RpcServerConnectionInfo connectionInfo, IReadOnlyList<RpcClientCallInterceptor> callInterceptors)
        {
            if (connectionInfo != null && Uri.TryCreate(connectionInfo.HostUrl, UriKind.Absolute, out var parsedUrl))
            {
                if (parsedUrl.Scheme == GrpcScheme)
                {
                    return new GrpcServerConnection(connectionInfo, this.credentials, this.proxyProvider, this.serializer, this.channelOptions, callInterceptors);
                }
            }


            throw new ArgumentException("Unsupported connection info. Use CanCreateConnection to check whether a connection can be created.");
        }
    }
}
