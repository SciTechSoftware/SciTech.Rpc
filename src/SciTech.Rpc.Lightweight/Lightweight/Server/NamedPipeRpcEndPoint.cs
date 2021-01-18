#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using SciTech.Rpc.Lightweight.Server.Internal;
using SciTech.Rpc.Server;
using SciTech.Threading;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
using System.IO.Pipes;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server
{

    public class NamedPipeRpcEndPoint : LightweightRpcEndPoint
    {
        private readonly Uri uri;

        public NamedPipeRpcEndPoint(Uri uri)
        {
            this.uri = uri ?? throw new ArgumentNullException(nameof(uri));
        }

        public NamedPipeRpcEndPoint(string pipeName)
            :this( new Uri( $"{WellKnownRpcSchemes.LightweightPipe}://./{pipeName}"))
        {
        }

        public override string DisplayName => this.uri.ToString();

        public override string HostName => this.uri.Host;

        public override RpcConnectionInfo GetConnectionInfo(RpcServerId serverId)
        {
            return new RpcConnectionInfo(this.DisplayName, this.uri, serverId);
        }

        protected internal override ILightweightRpcListener CreateListener(
            IRpcConnectionHandler connectionHandler,
            int maxRequestSize, int maxResponseSize)
        {
            return new RpcPipeServer(this, connectionHandler, maxRequestSize);
        }

        private class RpcPipeServer : NamedPipeServer, ILightweightRpcListener
        {
            private readonly NamedPipeRpcEndPoint endPoint;
            
            private readonly IRpcConnectionHandler connectionHandler;

            private readonly int maxRequestSize;
            
            /// <summary>
            /// Create a new instance of a named pipe server
            /// </summary>
            internal RpcPipeServer(NamedPipeRpcEndPoint endPoint, IRpcConnectionHandler connectionHandler, int maxRequestSize) 
                : base(endPoint.uri)
            {
                this.endPoint = endPoint;
                this.connectionHandler= connectionHandler;
                this.maxRequestSize = maxRequestSize;
            }

            public Task StopAsync()
            {
                this.Stop();

                return Task.CompletedTask;
            }

            protected override Task OnClientConnectedAsync(in ClientConnection client)
            {
                return this.connectionHandler.RunPipelineClientAsync(client.Transport, this.endPoint, null);//, client.CancellationToken);
            }

            protected override void OnClientFaulted(in ClientConnection client, Exception exception)
            {
                base.OnClientFaulted(client, exception);
            }

            protected override void OnServerFaulted(Exception exception)
            {
                base.OnServerFaulted(exception);
            }

            void ILightweightRpcListener.Listen() => this.Listen();
        }
    }
}
