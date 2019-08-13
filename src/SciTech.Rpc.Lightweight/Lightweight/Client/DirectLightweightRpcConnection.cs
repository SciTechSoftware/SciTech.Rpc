#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using SciTech.Rpc.Client;
using SciTech.Rpc.Lightweight.Client.Internal;
using SciTech.Threading;
using System;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Client
{
    public sealed class DirectLightweightRpcConnection : LightweightRpcConnection, IDisposable
    {
        private readonly object syncRoot = new object();

        private IDuplexPipe clientPipe;

        private volatile RpcPipelineClient? connectedClient;

        public DirectLightweightRpcConnection(
            RpcServerConnectionInfo connectionInfo,
            IDuplexPipe clientPipe,
            ImmutableRpcClientOptions? options,
            LightweightProxyProvider proxyGenerator,
            LightweightOptions? lightweightOptions = null)
            : base(connectionInfo, options, proxyGenerator, lightweightOptions)
        {
            this.clientPipe = clientPipe;

        }

        public override bool IsConnected => this.connectedClient != null;

        public override bool IsEncrypted => false;

        public override bool IsMutuallyAuthenticated => false;

        public override bool IsSigned => false;

        public void Dispose()
        {
            RpcPipelineClient? connectedClient;
            lock (this.syncRoot)
            {
                connectedClient = this.connectedClient;
                this.connectedClient = null;
            }

            if (connectedClient != null)
            {
                connectedClient.Dispose();
            }
        }

        public override async Task ShutdownAsync()
        {
            RpcPipelineClient? prevClient = this.ResetConnection(RpcConnectionState.Disconnected);

            if (prevClient != null)
            {
                await prevClient.AwaitFinished().ContextFree();
            }

            this.NotifyDisconnected();
        }

        internal override ValueTask<RpcPipelineClient> ConnectClientAsync()
        {
            RpcPipelineClient connectedClient;

            lock (this.syncRoot)
            {
                if (this.clientPipe == null)
                {
                    throw new ObjectDisposedException(this.ToString());
                }

                if (this.connectedClient == null)
                {
                    int sendMaxMessageSize = this.Options?.SendMaxMessageSize ?? DefaultMaxRequestMessageSize;
                    int receiveMaxMessageSize = this.Options?.ReceiveMaxMessageSize ?? DefaultMaxResponseMessageSize;

                    this.connectedClient = new RpcPipelineClient(this.clientPipe, sendMaxMessageSize, receiveMaxMessageSize, this.KeepSizeLimitedConnectionAlive);
                    this.connectedClient.ReceiveLoopFaulted += this.ConnectedClient_ReceiveLoopFaulted;
                    this.connectedClient.RunAsyncCore();
                }

                connectedClient = this.connectedClient;
            }

            return new ValueTask<RpcPipelineClient>(connectedClient);
        }

        private void ConnectedClient_ReceiveLoopFaulted(object sender, EventArgs e)
        {
            this.ResetConnection(RpcConnectionState.ConnectionLost);

            this.NotifyConnectionLost();
        }

        private RpcPipelineClient? ResetConnection(RpcConnectionState state)
        {
            RpcPipelineClient? connectedClient;

            lock (this.syncRoot)
            {
                connectedClient = this.connectedClient;

                this.connectedClient = null;

                this.SetConnectionState(state);
            }

            connectedClient?.Close(); 

            return connectedClient;
        }
    }
}
