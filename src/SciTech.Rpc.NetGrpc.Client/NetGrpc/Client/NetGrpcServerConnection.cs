#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using SciTech.Rpc.Client;
using SciTech.Rpc.NetGrpc.Client.Internal;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using GrpcCore = Grpc.Core;
using GrpcClient = Grpc.Net.Client;

namespace SciTech.Rpc.NetGrpc.Client
{
    //public class GrpcChannel : GrpcCore.ChannelBase
    //{
    //    /// <summary>
    //    /// Initializes a new instance of <see cref="GrpcChannel"/> class that connects to a specific host.
    //    /// </summary>
    //    /// <param name="target">Target of the channel.</param>
    //    protected GrpcChannel(string target) : base(target)
    //    {

    //    }

    //    public override GrpcCore.CallInvoker CreateCallInvoker()
    //    {
    //        throw new NotImplementedException();
    //    }
    //}

    public class NetGrpcServerConnection : RpcServerConnection
    {
        private bool isSecure;

        public NetGrpcServerConnection(
            RpcServerConnectionInfo connectionInfo,
            RpcClientServiceOptions? options = null,
            NetGrpcProxyProvider? proxyProvider = null,
            IRpcSerializer? serializer = null,            
            IReadOnlyList<RpcClientCallInterceptor>? callInterceptors = null)
            : base(connectionInfo, proxyProvider ?? NetGrpcProxyProvider.Default)
        {
            if (Uri.TryCreate(connectionInfo?.HostUrl, UriKind.Absolute, out var parsedUrl)
                && (parsedUrl.Scheme == NetGrpcConnectionProvider.GrpcScheme))
            {
                GrpcCore.ChannelCredentials actualCredentials = credentials;

                if (callInterceptors != null)
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

                        actualCredentials = GrpcCore.ChannelCredentials.Create(actualCredentials, callCredentials);
                    }
                }

                this.Channel = channel;// new GrpcCore.Channel(parsedUrl.Host, parsedUrl.Port, actualCredentials, channelOptions);

                this.CallInvoker = channel.CreateCallInvoker();
                this.Serializer = serializer ?? new ProtobufSerializer();

                this.isSecure = false;//credentials != null && credentials != GrpcCore.ChannelCredentials.Insecure;
            }
            else
            {
                throw new NotImplementedException($"NetGrpcServerConnection is only implemented for the '{nameof(NetGrpcConnectionProvider.GrpcScheme)}' scheme.");
            }
        }

        public GrpcClient.GrpcChannel? Channel { get; private set; }

        public override bool IsConnected => false;//this.Channel?.State == GrpcCore.ChannelState.Ready;

        /// <summary>
        /// Get a value indicating whether this connection is encrypted. The current implementation assumes
        /// that the connection is encrypted if it's connected and credentials have been supplied.
        /// </summary>
        public override bool IsEncrypted => this.IsConnected && this.isSecure;

        /// <summary>
        /// Get a value indicating whether this client and server is mutually authenticated. Not yet implemented,
        /// will always return false.
        /// </summary>
        public override bool IsMutuallyAuthenticated => false;

        /// <summary>
        /// Get a value indicating whether this connection is signed. The current implementation assumes
        /// that the connection is signed if it's connected and credentials have been supplied.
        /// </summary>
        public override bool IsSigned => this.IsConnected && this.isSecure;

        internal GrpcCore.CallInvoker? CallInvoker { get; private set; }

        internal IRpcSerializer Serializer { get; }

        public override Task ConnectAsync()
        {
            var channel = this.Channel;
            if (channel != null)
            {
                return channel.ConnectAsync();
            }
            else
            {
                throw new ObjectDisposedException(this.ToString());
            }
        }

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
