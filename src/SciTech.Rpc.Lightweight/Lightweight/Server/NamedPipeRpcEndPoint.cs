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

using Pipelines.Sockets.Unofficial;
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

        public override RpcServerConnectionInfo GetConnectionInfo(RpcServerId hostId)
        {
            return new RpcServerConnectionInfo(this.DisplayName, this.uri, hostId);
        }

        protected internal override ILightweightRpcListener CreateListener(Func<IDuplexPipe, CancellationToken, Task> clientConnectedCallback, int maxRequestSize, int maxResponseSize)
        {
            return new RpcPipeServer(this.uri, clientConnectedCallback, maxRequestSize);
        }

        private class RpcPipeServer : NamedPipeServer, ILightweightRpcListener
        {
            private Func<IDuplexPipe, CancellationToken, Task> clientConnectedCallback;
            
            private readonly int maxRequestSize;
            
            /// <summary>
            /// Create a new instance of a named pipe server
            /// </summary>
            internal RpcPipeServer(Uri uri, Func<IDuplexPipe, CancellationToken, Task> clientConnectedCallback, int maxRequestSize) 
                : base(uri)
            {
                this.clientConnectedCallback = clientConnectedCallback;
                this.maxRequestSize = maxRequestSize;
            }

            public void Listen()
            {
                int receivePauseThreshold = Math.Max(this.maxRequestSize, 65536);
                var receiveOptions = new System.IO.Pipelines.PipeOptions(
                    pauseWriterThreshold: receivePauseThreshold,
                    resumeWriterThreshold: receivePauseThreshold / 2,
                    readerScheduler: PipeScheduler.Inline,
                    useSynchronizationContext: false);
                var sendOptions = new System.IO.Pipelines.PipeOptions(
                    readerScheduler: PipeScheduler.ThreadPool,
                    useSynchronizationContext: false);

                base.Listen(sendOptions: sendOptions, receiveOptions: receiveOptions);
            }

            public Task StopAsync()
            {
                this.Stop();

                return Task.CompletedTask;
            }

            protected override Task OnClientConnectedAsync(in ClientConnection client)
            {
                return this.clientConnectedCallback(client.Transport, client.CancellationToken);
            }

            protected override void OnClientFaulted(in ClientConnection client, Exception exception)
            {
                base.OnClientFaulted(client, exception);
            }

            protected override void OnServerFaulted(Exception exception)
            {
                base.OnServerFaulted(exception);
            }
        }

    }
}
