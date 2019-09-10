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
using SciTech.Rpc.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.Grpc.Client
{
    public class GrpcServerConnection : RpcServerConnection, IGrpcServerConnection
    {

        private static readonly ILog Logger = LogProvider.For<GrpcServerConnection>();

        private bool isSecure;

        public GrpcServerConnection(
            RpcServerConnectionInfo connectionInfo,
            ImmutableRpcClientOptions? options = null,
            IRpcProxyDefinitionsProvider? definitionsProvider = null,
            IEnumerable<GrpcCore.ChannelOption>? channelOptions = null)
            : this(connectionInfo,
                  GrpcCore.ChannelCredentials.Insecure,
                  options,
                  definitionsProvider,
                  channelOptions)
        {
        }

        public GrpcServerConnection(
            RpcServerConnectionInfo connectionInfo,
            GrpcCore.ChannelCredentials credentials,
            ImmutableRpcClientOptions? options = null,
            IRpcProxyDefinitionsProvider? definitionsProvider = null,
            IEnumerable<GrpcCore.ChannelOption>? channelOptions = null)
            :this( 
                 connectionInfo, credentials, options, 
                 GrpcProxyGenerator.Factory.CreateProxyGenerator(definitionsProvider),
                 channelOptions )
        {
        }

        internal GrpcServerConnection(
            RpcServerConnectionInfo connectionInfo,
            GrpcCore.ChannelCredentials credentials,
            ImmutableRpcClientOptions? options,
            GrpcProxyGenerator proxyGenerator,
            IEnumerable<GrpcCore.ChannelOption>? channelOptions)
            : base(connectionInfo, options, proxyGenerator)
        {
            if (Uri.TryCreate(connectionInfo?.HostUrl, UriKind.Absolute, out var parsedUrl)
                && (parsedUrl.Scheme == GrpcConnectionProvider.GrpcScheme))
            {
                GrpcCore.ChannelCredentials actualCredentials = credentials;

                if (options != null)
                {
                    var callInterceptors = options.Interceptors;
                    if (callInterceptors != null)
                    {
                        int nInterceptors = callInterceptors.Length;
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
                }

                var allOptions = ExtractOptions(options, channelOptions);

                this.Channel = new GrpcCore.Channel(parsedUrl.Host, parsedUrl.Port, actualCredentials, allOptions);

                this.CallInvoker = new GrpcCore.DefaultCallInvoker(this.Channel);

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

        GrpcCore.CallInvoker? IGrpcServerConnection.CallInvoker => this.CallInvoker;

        IRpcSerializer IGrpcServerConnection.Serializer => this.Serializer;

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

        protected override IRpcSerializer CreateDefaultSerializer() => new ProtobufSerializer();

        private static IEnumerable<GrpcCore.ChannelOption>? ExtractOptions(ImmutableRpcClientOptions? options, IEnumerable<GrpcCore.ChannelOption>? channelOptions)
        {
            if (options == null)
            {
                return channelOptions;
            }

            List<GrpcCore.ChannelOption> allChannelOptions = new List<GrpcCore.ChannelOption>();
            if (channelOptions != null)
            {
                allChannelOptions.AddRange(channelOptions);
            }

            if (options?.SendMaxMessageSize != null)
            {
                if (allChannelOptions.Find(o => o.Name == GrpcCore.ChannelOptions.MaxSendMessageLength) == null)
                {
                    allChannelOptions.Add(new GrpcCore.ChannelOption(GrpcCore.ChannelOptions.MaxSendMessageLength, options.SendMaxMessageSize.Value));
                }
                else
                {
                    Logger.Warn($"MaxSendMessageLength is already specified by ChannelOptions, ignoring {nameof(options.SendMaxMessageSize)} in {nameof(RpcClientOptions)}.");
                }
            }

            if (options?.ReceiveMaxMessageSize != null)
            {
                if (allChannelOptions.Find(o => o.Name == GrpcCore.ChannelOptions.MaxReceiveMessageLength) == null)
                {
                    allChannelOptions.Add(new GrpcCore.ChannelOption(GrpcCore.ChannelOptions.MaxReceiveMessageLength, options.ReceiveMaxMessageSize.Value));
                }
                else
                {
                    Logger.Warn($"MaxReceiveMessageLength is already specified by ChannelOptions, ignoring {nameof(options.ReceiveMaxMessageSize)} in {nameof(RpcClientOptions)}.");
                }
            }

            return allChannelOptions;
        }

    }
}
