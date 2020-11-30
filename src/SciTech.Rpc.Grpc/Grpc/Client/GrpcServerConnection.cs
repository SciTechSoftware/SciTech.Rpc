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
using SciTech.Rpc.Serialization;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.Grpc.Client
{
    public class GrpcServerConnection : RpcChannel, IGrpcRpcChannel
    {
        private bool isSecure;

        public GrpcServerConnection(
            RpcServerConnectionInfo connectionInfo,
            IRpcClientOptions? options = null,
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
            IRpcClientOptions? options = null,
            IRpcProxyDefinitionsProvider? definitionsProvider = null,
            IEnumerable<GrpcCore.ChannelOption>? channelOptions = null)
            : this(
                 connectionInfo, credentials, options,
                 GrpcProxyGenerator.Default,
                 channelOptions)
        {
        }

        internal GrpcServerConnection(
            RpcServerConnectionInfo connectionInfo,
            GrpcCore.ChannelCredentials credentials,
            IRpcClientOptions? options,
            GrpcProxyGenerator proxyGenerator,
            IEnumerable<GrpcCore.ChannelOption>? channelOptions)
            : base(connectionInfo, options, proxyGenerator)
        {
            if (connectionInfo?.HostUrl?.Scheme == GrpcConnectionProvider.GrpcScheme)
            {
                GrpcCore.ChannelCredentials actualCredentials = credentials;

                if (options != null)
                {
                    var callInterceptors = options.Interceptors;
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
                }

                var allOptions = ExtractOptions(options, channelOptions);

                this.Channel = new GrpcCore.Channel(connectionInfo.HostUrl.Host, connectionInfo.HostUrl.Port, actualCredentials, allOptions);

                this.CallInvoker = new GrpcCore.DefaultCallInvoker(this.Channel);

                this.isSecure = credentials != null && credentials != GrpcCore.ChannelCredentials.Insecure;
            }
            else
            {
                throw new NotImplementedException($"GrpcServerConnection is only implemented for the '{nameof(GrpcConnectionProvider.GrpcScheme)}' scheme.");
            }
        }

        public GrpcCore.Channel? Channel { get; private set; }


        internal GrpcCore.CallInvoker? CallInvoker { get; private set; }

        GrpcCore.CallInvoker? IGrpcRpcChannel.CallInvoker => this.CallInvoker;

        IRpcSerializer IGrpcRpcChannel.Serializer => this.Serializer;

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            var channel = this.Channel;
            if (channel != null)
            {
                Task connectTask = channel.ConnectAsync();
                if (!connectTask.IsCompleted && cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(() => channel.ShutdownAsync().Forget());
                }

                return connectTask;
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

        protected override IRpcSerializer CreateDefaultSerializer() => new ProtobufRpcSerializer();

        private static IEnumerable<GrpcCore.ChannelOption>? ExtractOptions(IRpcClientOptions? options, IEnumerable<GrpcCore.ChannelOption>? channelOptions)
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
                    // TODO: Logger.Warn($"MaxSendMessageLength is already specified by ChannelOptions, ignoring {nameof(options.SendMaxMessageSize)} in {nameof(RpcClientOptions)}.");
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
                    // TODO: Logger.Warn($"MaxReceiveMessageLength is already specified by ChannelOptions, ignoring {nameof(options.ReceiveMaxMessageSize)} in {nameof(RpcClientOptions)}.");
                }
            }

            return allChannelOptions;
        }

    }
}
