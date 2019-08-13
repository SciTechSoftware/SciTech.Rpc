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
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Client
{
    public abstract class LightweightRpcConnection : RpcServerConnection
    {
        public const int DefaultMaxRequestMessageSize = 4 * 1024 * 1024;

        public const int DefaultMaxResponseMessageSize = 4 * 1024 * 1024;

        protected LightweightRpcConnection(
            RpcServerConnectionInfo connectionInfo,
            ImmutableRpcClientOptions? options = null,
            LightweightProxyProvider? proxyProvider = null,
            LightweightOptions? lightweightOptions = null)
            : base(connectionInfo, options, proxyProvider ?? LightweightProxyProvider.Default)
        {
            this.KeepSizeLimitedConnectionAlive = lightweightOptions?.KeepSizeLimitedConnectionAlive ?? true;
        }

        public bool KeepSizeLimitedConnectionAlive { get; }

        public override Task ConnectAsync() => this.ConnectClientAsync().AsTask();

        internal abstract ValueTask<RpcPipelineClient> ConnectClientAsync();

        protected override IRpcSerializer CreateDefaultSerializer()
        {
            return new DataContractRpcSerializer();
        }
    }
}
