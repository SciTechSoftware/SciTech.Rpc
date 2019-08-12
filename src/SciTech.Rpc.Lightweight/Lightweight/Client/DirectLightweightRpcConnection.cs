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
using System;
using System.Collections.Generic;
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
            LightweightProxyProvider proxyGenerator)
            : base(connectionInfo, options, proxyGenerator)
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

        public override Task ShutdownAsync()
        {
            RpcPipelineClient? connectedClient;
            lock (this.syncRoot)
            {
                connectedClient = this.connectedClient;
                this.connectedClient = null;
            }

            if (connectedClient != null)
            {
                connectedClient.Close();
                return connectedClient.AwaitFinished();
            }

            return Task.CompletedTask;
        }
        
        internal override Task<RpcPipelineClient> ConnectClientAsync()
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
                    this.connectedClient = new RpcPipelineClient(this.clientPipe);
                }

                connectedClient = this.connectedClient;
            }

            return Task.FromResult(connectedClient);
        }
    }
}
