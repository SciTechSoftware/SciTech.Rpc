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
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Client
{
    public sealed class DirectLightweightRpcConnection : LightweightRpcConnection
    {
        private IDuplexPipe? clientPipe;

        public DirectLightweightRpcConnection(
            RpcServerConnectionInfo connectionInfo,
            IDuplexPipe clientPipe,
            IRpcClientOptions? options=null,
            IRpcProxyDefinitionsProvider? definitionsProvider=null,
            LightweightOptions? lightweightOptions = null)
            : base(connectionInfo, options, 
                  LightweightProxyGenerator.Factory.CreateProxyGenerator(definitionsProvider), 
                  lightweightOptions)
        {
            this.clientPipe = clientPipe;

        }

        public override bool IsEncrypted => false;

        public override bool IsMutuallyAuthenticated => false;

        public override bool IsSigned => false;

        protected override Task<IDuplexPipe> ConnectPipelineAsync(int sendMaxMessageSize, int receiveMaxMessageSize, CancellationToken cancellationToken)

        {
            var pipe = this.clientPipe ?? throw new ObjectDisposedException(this.ToString());
            this.clientPipe = null;
            return Task.FromResult(pipe);
        }

        protected override void OnConnectionResetSynchronized()
        {
            base.OnConnectionResetSynchronized();

            (this.clientPipe as IDisposable)?.Dispose();
            this.clientPipe = null;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            (this.clientPipe as IDisposable)?.Dispose();
            this.clientPipe = null;
        }
    }
}
