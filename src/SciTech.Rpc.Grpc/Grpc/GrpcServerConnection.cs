using SciTech.Rpc.Client;
using SciTech.Rpc.Grpc.Client.Internal;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.Grpc.Client
{
    public class GrpcServerConnection : RpcServerConnection
    {
        public GrpcServerConnection(RpcServerConnectionInfo connectionInfo,
            GrpcProxyProvider? proxyProvider = null,
            IRpcSerializer? serializer = null,
            IEnumerable<GrpcCore.ChannelOption>? channelOptions = null,
            IReadOnlyList<RpcClientCallInterceptor>? callInterceptors = null)
            : this(connectionInfo, 
                  GrpcCore.ChannelCredentials.Insecure, 
                  proxyProvider, serializer,
                  channelOptions,
                  callInterceptors)
        {
        }

        public GrpcServerConnection(RpcServerConnectionInfo connectionInfo,
            GrpcCore.ChannelCredentials credentials,
            GrpcProxyProvider? proxyProvider = null,
            IRpcSerializer? serializer = null,            
            IEnumerable<GrpcCore.ChannelOption>? channelOptions=null,
            IReadOnlyList<RpcClientCallInterceptor>? callInterceptors = null)
            : base(connectionInfo, proxyProvider ?? GrpcProxyProvider.Default)
        {
            if (Uri.TryCreate(connectionInfo?.HostUrl, UriKind.Absolute, out var parsedUrl)
                && (parsedUrl.Scheme == GrpcConnectionProvider.GrpcScheme))
            {
                if (callInterceptors != null )
                {
                    int nInterceptors = callInterceptors.Count;
                    if (nInterceptors > 0)
                    {
                        GrpcCore.CallCredentials callCredentials;
                        if (nInterceptors > 1)
                        {
                            GrpcCore.CallCredentials[] allCallCredentials = new GrpcCore.CallCredentials[nInterceptors];
                            for (int index = 0; index < nInterceptors; index++)
                            {
                                var callInterceptor = callInterceptors[index];
                                allCallCredentials[index] = GrpcCore.CallCredentials.FromInterceptor((context, metadata) => callInterceptor(new GrpcCallMetadata(metadata)));
                            }

                            callCredentials = GrpcCore.CallCredentials.Compose(allCallCredentials);
                        }
                        else
                        {
                            var callInterceptor = callInterceptors[0];
                            callCredentials = GrpcCore.CallCredentials.FromInterceptor((context, metadata) => callInterceptor(new GrpcCallMetadata(metadata)));
                        }

                        credentials = GrpcCore.ChannelCredentials.Create(credentials, callCredentials);
                    }
                }

                this.Channel = new GrpcCore.Channel(parsedUrl.Host, parsedUrl.Port, credentials, channelOptions);

                this.CallInvoker = new GrpcCore.DefaultCallInvoker(this.Channel);
                this.Serializer = serializer ?? new ProtobufSerializer();
            }
            else
            {
                throw new NotImplementedException($"GrpcServerConnection is only implemented for the '{nameof(GrpcConnectionProvider.GrpcScheme)}' scheme.");
            }
        }

        public GrpcCore.Channel? Channel { get; private set; }

        internal GrpcCore.CallInvoker? CallInvoker { get; private set; }

        internal IRpcSerializer Serializer { get; }

        public override Task ShutdownAsync()
        {
            var channel = this.Channel;
            this.Channel = null;
            this.CallInvoker = null;

            if (channel != null)
            {
                return channel.ShutdownAsync();
            }
            else
            {
                return Task.CompletedTask;
            }
        }
    }
}
