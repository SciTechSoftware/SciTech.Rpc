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
using System;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server
{
    public sealed class DirectLightweightRpcEndPoint : LightweightRpcEndPoint
    {
        private readonly object syncRoot = new object();

        private IDuplexPipe? clientPipe;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="clientPipe"></param>
        public DirectLightweightRpcEndPoint(IDuplexPipe clientPipe)
        {
            this.clientPipe = clientPipe;
        }

        public override string DisplayName => "Direct";

        public override string HostName => "direct";

        public override RpcServerConnectionInfo GetConnectionInfo(RpcServerId serverId)
        {
            return new RpcServerConnectionInfo("Direct", new Uri("direct://localhost"), serverId);
        }

        protected internal override ILightweightRpcListener CreateListener(Func<IDuplexPipe, Task> clientConnectedCallback, int maxRequestSize, int maxResponseSize)
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

            return new DirectListener(pipe, clientConnectedCallback);
        }

        private class DirectListener : ILightweightRpcListener
        {
            private Func<IDuplexPipe, Task> clientConnectedCallback;

            private bool isListening;

            private IDuplexPipe pipe;

            internal DirectListener(IDuplexPipe pipe, Func<IDuplexPipe, Task> clientConnectedCallback)
            {
                this.pipe = pipe;
                this.clientConnectedCallback = clientConnectedCallback;
            }

            public void Listen()
            {
                if (this.isListening) throw new InvalidOperationException("DirectListener is already listening or has been stopped.");
                this.isListening = true;
                this.clientConnectedCallback(this.pipe);
            }

            public Task StopAsync()
            {
                if (!this.isListening) throw new InvalidOperationException("DirectListener is not listening.");

                return Task.CompletedTask;
            }
        }
    }
}
