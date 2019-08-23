using SciTech.Collections;
using SciTech.Rpc.Client;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using GrpcCore = Grpc.Core;
using GrpcNet = Grpc.Net;

namespace SciTech.Rpc.NetGrpc.Client
{
    public class NetGrpcConnectionProvider : IRpcConnectionProvider
    {
        public const string GrpcScheme = "grpc";

        private readonly NetGrpcProxyProvider proxyProvider;

        private readonly IRpcSerializer serializer;

        private readonly GrpcCore.ChannelCredentials credentials;

        GrpcNet.Client.GrpcChannelOptions channelOptions;

        public NetGrpcConnectionProvider(
            
            NetGrpcProxyProvider? proxyGenerator = null, 
            IRpcSerializer? serializer = null) 
            : this( GrpcCore.ChannelCredentials.Insecure, null, proxyGenerator, serializer )
        {
        }

        public NetGrpcConnectionProvider(
            RpcClientServiceOptions? options,
            //ChannelBuilder, IEnumerable<GrpcCore.ChannelOption>? channelOptions=null,
            NetGrpcProxyProvider? proxyGenerator = null, IRpcSerializer? serializer = null)
        {
            options.
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
                    return new NetGrpcServerConnection(connectionInfo, this.credentials, this.proxyProvider, this.serializer, this.channelOptions, callInterceptors);
                }
            }


            throw new ArgumentException("Unsupported connection info. Use CanCreateConnection to check whether a connection can be created.");
        }
    }
}
