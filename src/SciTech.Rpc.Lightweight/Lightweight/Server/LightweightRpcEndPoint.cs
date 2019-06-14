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

using SciTech.Rpc.Server;
using SciTech.Threading;
using System;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server
{
    public interface ILightweightRpcEndPoint : IRpcServerEndPoint
    {
        void Start(Func<IDuplexPipe, Task> clientConnectedCallback);

        Task StopAsync();
    }

    public sealed class DirectLightweightRpcEndPoint : ILightweightRpcEndPoint
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

        public string DisplayName => "Direct";

        public string HostName => "direct";

        public RpcServerConnectionInfo GetConnectionInfo(RpcServerId serverId)
        {
            return new RpcServerConnectionInfo("Direct", new Uri("direct://localhost"), serverId);
        }

        public Task StopAsync()
        {
            return Task.CompletedTask;
        }

        private async void RunClient(Task clientTask, IDuplexPipe pipe)
        {
#pragma warning disable CA1031 // Do not catch general exception types
            try
            {
                await clientTask.ContextFree();
                pipe.Input.Complete();
                pipe.Output.Complete();
            }
            catch (Exception e)
            {
                pipe.Input.Complete(e);
                pipe.Output.Complete(e);
            }
#pragma warning restore CA1031 // Do not catch general exception types
        }

        void ILightweightRpcEndPoint.Start(Func<IDuplexPipe, Task> clientConnectedCallback)
        {
            IDuplexPipe pipe;
            lock (this.syncRoot)
            {
                if (this.clientPipe == null)
                {
                    throw new InvalidOperationException("A DirectLightweightRpcEndPoint can only be started once.");
                }
                pipe = this.clientPipe;
                this.clientPipe = null;
            }

            clientConnectedCallback(pipe);
        }
    }
}
