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
using SciTech.Rpc.Grpc.Client.Internal;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.Grpc.Client
{
    public class GrpcServerConnection : RpcServerConnection
    {
        private bool isSecure;

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
            IEnumerable<GrpcCore.ChannelOption>? channelOptions = null,
            IReadOnlyList<RpcClientCallInterceptor>? callInterceptors = null)
            : base(connectionInfo, proxyProvider ?? GrpcProxyProvider.Default)
        {
            if (Uri.TryCreate(connectionInfo?.HostUrl, UriKind.Absolute, out var parsedUrl)
                && (parsedUrl.Scheme == GrpcConnectionProvider.GrpcScheme))
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

                this.Channel = new GrpcCore.Channel(parsedUrl.Host, parsedUrl.Port, actualCredentials, channelOptions);

                this.CallInvoker = new GrpcCore.DefaultCallInvoker(this.Channel);
                this.Serializer = serializer ?? new ProtobufSerializer();

                this.isSecure = credentials != null && credentials != GrpcCore.ChannelCredentials.Insecure;
            }
            else
            {
                throw new NotImplementedException($"GrpcServerConnection is only implemented for the '{nameof(GrpcConnectionProvider.GrpcScheme)}' scheme.");
            }
        }

        public GrpcCore.Channel? Channel { get; private set; }

        public override bool IsConnected => this.Channel?.State == GrpcCore.ChannelState.Ready;

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
