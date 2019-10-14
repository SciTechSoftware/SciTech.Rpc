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
using SciTech.Rpc.Logging;
using SciTech.Rpc.NetGrpc.Client.Internal;
using SciTech.Rpc.Serialization;
using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using GrpcCore = Grpc.Core;
using GrpcNet = Grpc.Net;

namespace SciTech.Rpc.NetGrpc.Client
{
    public class NetGrpcServerConnection : RpcChannel, IGrpcRpcChannel
    {
        private static readonly ILog Logger = LogProvider.For<NetGrpcServerConnection>();

        private bool isSecure;

        public NetGrpcServerConnection(
            string url,
            IRpcClientOptions? options = null,
            IRpcProxyDefinitionsProvider? definitionsProvider = null,
            GrpcNet.Client.GrpcChannelOptions? channelOptions = null)
            : this(
                  new RpcServerConnectionInfo(new Uri(url)),
                  options,
                  GrpcProxyGenerator.Factory.CreateProxyGenerator(definitionsProvider),
                  channelOptions)
        {
        }
        public NetGrpcServerConnection(
            Uri url,
            IRpcClientOptions? options = null,
            IRpcProxyDefinitionsProvider? definitionsProvider = null,
            GrpcNet.Client.GrpcChannelOptions? channelOptions = null)
            : this(
                  new RpcServerConnectionInfo(url),
                  options,
                  GrpcProxyGenerator.Factory.CreateProxyGenerator(definitionsProvider),
                  channelOptions)
        {
        }

        public NetGrpcServerConnection(
            RpcServerConnectionInfo connectionInfo,
            IRpcClientOptions? options = null,
            IRpcProxyDefinitionsProvider? definitionsProvider = null,
            GrpcNet.Client.GrpcChannelOptions? channelOptions = null)
            : this(connectionInfo, options, GrpcProxyGenerator.Factory.CreateProxyGenerator(definitionsProvider),
                channelOptions)
        {
        }

        internal NetGrpcServerConnection(
            RpcServerConnectionInfo connectionInfo,
            IRpcClientOptions? options,
            GrpcProxyGenerator proxyGenerator,
            GrpcNet.Client.GrpcChannelOptions? channelOptions)
            : base(connectionInfo, options, proxyGenerator)
        {
            if (connectionInfo is null) throw new ArgumentNullException(nameof(connectionInfo));

            var scheme = connectionInfo.HostUrl?.Scheme;
            if (connectionInfo.HostUrl != null
                && (scheme == WellKnownRpcSchemes.Grpc || scheme == "https" || scheme == "http" ))
            {
                GrpcNet.Client.GrpcChannelOptions actualChannelOptions = ExtractOptions(options, channelOptions);

                this.isSecure = scheme == "https" || scheme == WellKnownRpcSchemes.Grpc;

                var interceptors = options?.Interceptors ?? ImmutableList<RpcClientCallInterceptor>.Empty;
                int nInterceptors = interceptors.Count;
                if (nInterceptors > 0)
                {
                    GrpcCore.CallCredentials callCredentials;
                    if (nInterceptors > 1)
                    {
                        GrpcCore.CallCredentials[] allCallCredentials = new GrpcCore.CallCredentials[nInterceptors];
                        for (int index = 0; index < nInterceptors; index++)
                        {
                            var callInterceptor = interceptors[index];
                            allCallCredentials[index] = GrpcCore.CallCredentials.FromInterceptor((context, metadata) => callInterceptor(new GrpcCallMetadata(metadata)));
                        }

                        callCredentials = GrpcCore.CallCredentials.Compose(allCallCredentials);
                    }
                    else
                    {
                        var callInterceptor = interceptors[0];
                        callCredentials = GrpcCore.CallCredentials.FromInterceptor((context, metadata) => callInterceptor(new GrpcCallMetadata(metadata)));
                    }

                    var channelCredentials = actualChannelOptions.Credentials;
                    if( channelCredentials == null )
                    {
                        if( this.isSecure )
                        {
                            channelCredentials = new GrpcCore.SslCredentials();
                        } else
                        {
                            channelCredentials = GrpcCore.ChannelCredentials.Insecure;
                        }
                    }

                    actualChannelOptions.Credentials = GrpcCore.ChannelCredentials.Create(channelCredentials, callCredentials);
                }


                var channelUri = scheme == WellKnownRpcSchemes.Grpc
                    ? new Uri($"https://{connectionInfo.HostUrl.Authority}/")
                    : connectionInfo.HostUrl;

                this.Channel = GrpcNet.Client.GrpcChannel.ForAddress(channelUri, actualChannelOptions);

                this.CallInvoker = this.Channel.CreateCallInvoker();

                
            }
            else
            {
                throw new NotImplementedException($"NetGrpcServerConnection is only implemented for the '{WellKnownRpcSchemes.Grpc}' scheme.");
            }
        }

        public GrpcNet.Client.GrpcChannel? Channel { get; private set; }

        internal GrpcCore.CallInvoker? CallInvoker { get; private set; }

        GrpcCore.CallInvoker? IGrpcRpcChannel.CallInvoker => this.CallInvoker;

        IRpcSerializer IGrpcRpcChannel.Serializer => this.Serializer;


        public override Task ShutdownAsync()
        {
            var channel = this.Channel;
            this.Channel = null;
            this.CallInvoker = null;

            if (channel != null)
            {
                channel.Dispose();
            }

            return Task.CompletedTask;
        }

        protected override IRpcSerializer CreateDefaultSerializer() => new ProtobufRpcSerializer();

        private static GrpcNet.Client.GrpcChannelOptions ExtractOptions(IRpcClientOptions? options, GrpcNet.Client.GrpcChannelOptions? channelOptions)
        {
            if (channelOptions != null && options == null)
            {
                return channelOptions;
            }

            var extractedOptions = new GrpcNet.Client.GrpcChannelOptions();

            if (channelOptions != null)
            {
                var o = extractedOptions;

                o.CompressionProviders = channelOptions.CompressionProviders;
                o.Credentials = channelOptions.Credentials;
                o.DisposeHttpClient = channelOptions.DisposeHttpClient;
                o.HttpClient = channelOptions.HttpClient;
                o.LoggerFactory = channelOptions.LoggerFactory;
                o.MaxReceiveMessageSize = channelOptions.MaxReceiveMessageSize;
                o.MaxSendMessageSize = channelOptions.MaxSendMessageSize;
                o.ThrowOperationCanceledOnCancellation = channelOptions.ThrowOperationCanceledOnCancellation;
            }

            if (options?.SendMaxMessageSize != null)
            {
                if (extractedOptions.MaxSendMessageSize == null)
                {
                    extractedOptions.MaxSendMessageSize = options.SendMaxMessageSize;
                }
                else
                {
                    Logger.Warn($"MaxSendMessageLength is already specified by ChannelOptions, ignoring {nameof(options.SendMaxMessageSize)} in {nameof(RpcClientOptions)}.");
                }
            }

            if (options?.ReceiveMaxMessageSize != null)
            {
                if (extractedOptions.MaxReceiveMessageSize == null)
                {
                    extractedOptions.MaxReceiveMessageSize = options.ReceiveMaxMessageSize;
                }
                else
                {
                    Logger.Warn($"MaxReceiveMessageLength is already specified by ChannelOptions, ignoring {nameof(options.ReceiveMaxMessageSize)} in {nameof(RpcClientOptions)}.");
                }
            }

            return extractedOptions;
        }

    }
}
