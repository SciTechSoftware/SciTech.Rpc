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

using SciTech.Rpc.Lightweight.Server.Internal;
using SciTech.Threading;
using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server
{
    public sealed class InprocRpcEndPoint : LightweightRpcEndPoint
    {
        private readonly object syncRoot = new object();

        private IDuplexPipe? clientPipe;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="clientPipe"></param>
        public InprocRpcEndPoint(IDuplexPipe clientPipe)
        {
            this.clientPipe = clientPipe;
        }

        public override string DisplayName => "Direct";

        public override string HostName => "direct";

        public override RpcConnectionInfo GetConnectionInfo(RpcServerId serverId)
        {
            return new RpcConnectionInfo("Direct", new Uri("direct://localhost"), serverId);
        }

        protected internal override ILightweightRpcListener CreateListener(
            IRpcConnectionHandler connectionHandler,
            int maxRequestSize, int maxResponseSize)
        {
            IDuplexPipe pipe;
            lock (this.syncRoot)
            {
                if (this.clientPipe == null)
                {
                    throw new InvalidOperationException("A DirectLightweightRpcEndPoint listener can only be created once.");
                }
                pipe = this.clientPipe;
                this.clientPipe = null;
            }

            return new DirectListener(this, pipe, connectionHandler);
        }

        private class DirectListener : ILightweightRpcListener
        {
            private readonly InprocRpcEndPoint endPoint;

            private readonly IRpcConnectionHandler connectionHandler;

            private bool isListening;

            private IDuplexPipe? pipe;

            internal DirectListener(InprocRpcEndPoint endPoint, IDuplexPipe pipe, IRpcConnectionHandler connectionHandler)
            {
                this.endPoint = endPoint;
                this.pipe = pipe;
                this.connectionHandler = connectionHandler;
            }

            public async ValueTask DisposeAsync()
            {
                if( this.isListening )
                {
                    await this.StopAsync().ContextFree();
                }

                (this.pipe as IDisposable)?.Dispose();
                this.pipe = null;
            }

            public void Listen()
            {
                if (this.isListening || this.pipe == null) throw new InvalidOperationException("DirectListener is already listening or has been stopped.");
                this.isListening = true;

                this.connectionHandler.RunPipelineClientAsync(this.pipe, this.endPoint, Thread.CurrentPrincipal);//, this.clientCts.Token);
                this.pipe = null;
            }

            public Task StopAsync()
            {
                if (!this.isListening) throw new InvalidOperationException("DirectListener is not listening.");

                return Task.CompletedTask;
            }
        }
    }
}
