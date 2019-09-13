﻿#region Copyright notice and license
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
using System;
using System.Threading;
using System.Threading.Tasks;
using GrpcCore = Grpc.Core;
using GrpcNet = Grpc.Net;

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

    public class NetGrpcServerConnection : RpcServerConnection, IGrpcServerConnection
    {

        private static readonly ILog Logger = LogProvider.For<NetGrpcServerConnection>();

        private bool isSecure;

        public NetGrpcServerConnection(
            RpcServerConnectionInfo connectionInfo,
            ImmutableRpcClientOptions? options = null,
            IRpcProxyDefinitionsProvider? definitionsProvider = null,
            GrpcNet.Client.GrpcChannelOptions? channelOptions = null)
            : this(connectionInfo, options, GrpcProxyGenerator.Factory.CreateProxyGenerator(definitionsProvider),
                channelOptions)
        {
        }

        internal NetGrpcServerConnection(
            RpcServerConnectionInfo connectionInfo,
            ImmutableRpcClientOptions? options,
            GrpcProxyGenerator proxyGenerator,
            GrpcNet.Client.GrpcChannelOptions? channelOptions)
            : base(connectionInfo, options, proxyGenerator)
        {
            if (connectionInfo?.HostUrl?.Scheme == NetGrpcConnectionProvider.GrpcScheme)
            {
                GrpcNet.Client.GrpcChannelOptions actualChannelOptions = ExtractOptions(options, channelOptions);

                //var interceptors = options?.Interceptors ?? ImmutableArray<RpcClientCallInterceptor>.Empty;
                //if (interceptors.Length > 0)
                //{
                //    GrpcCore.CallCredentials callCredentials;
                //    if (nInterceptors > 1)
                //    {
                //        GrpcCore.CallCredentials[] allCallCredentials = new GrpcCore.CallCredentials[nInterceptors];
                //        for (int index = 0; index < nInterceptors; index++)
                //        {
                //            var callInterceptor = interceptors[index];
                //            allCallCredentials[index] = GrpcCore.CallCredentials.FromInterceptor((context, metadata) => callInterceptor(new GrpcCallMetadata(metadata)));
                //        }

                //        callCredentials = GrpcCore.CallCredentials.Compose(allCallCredentials);
                //    }
                //    else
                //    {
                //        var callInterceptor = callInterceptors[0];
                //        callCredentials = GrpcCore.CallCredentials.FromInterceptor((context, metadata) => callInterceptor(new GrpcCallMetadata(metadata)));
                //    }

                //    actualCredentials = GrpcCore.ChannelCredentials.Create(actualCredentials, callCredentials);
                //}



                this.Channel = GrpcNet.Client.GrpcChannel.ForAddress(new Uri($"https://{connectionInfo.HostUrl.Authority}/"), actualChannelOptions);
                
                this.CallInvoker = this.Channel.CreateCallInvoker();

                this.isSecure = false;//credentials != null && credentials != GrpcCore.ChannelCredentials.Insecure;
            }
            else
            {
                throw new NotImplementedException($"NetGrpcServerConnection is only implemented for the '{nameof(NetGrpcConnectionProvider.GrpcScheme)}' scheme.");
            }
        }

        public GrpcNet.Client.GrpcChannel? Channel { get; private set; }

        public override bool IsConnected => false;// this.Channel?.State == GrpcCore.ChannelState.Ready;

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

        public override Task ConnectAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
            //var channel = this.Channel;
            //if (channel != null)
            //{
            //    return channel.ConnectAsync();
            //}
            //else
            //{
            //    throw new ObjectDisposedException(this.ToString());
            //}
        }

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

        protected override IRpcSerializer CreateDefaultSerializer() => new ProtobufSerializer();

        private static GrpcNet.Client.GrpcChannelOptions ExtractOptions(ImmutableRpcClientOptions? options, GrpcNet.Client.GrpcChannelOptions? channelOptions)
        {
            if( channelOptions != null && options == null )
            {
                return channelOptions;
            }

            var extractedOptions = new GrpcNet.Client.GrpcChannelOptions();

            if (channelOptions != null)
            {
                var o = extractedOptions;

                o.CompressionProviders = channelOptions.CompressionProviders;
                o.Credentials = channelOptions.Credentials;
                o.DisposeHttpClient= channelOptions.DisposeHttpClient;
                o.HttpClient = channelOptions.HttpClient;
                o.LoggerFactory= channelOptions.LoggerFactory;
                o.MaxReceiveMessageSize= channelOptions.MaxReceiveMessageSize;
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
