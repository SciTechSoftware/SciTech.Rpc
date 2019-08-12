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
        protected LightweightRpcConnection(
            RpcServerConnectionInfo connectionInfo,
            ImmutableRpcClientOptions? options = null,
            LightweightProxyProvider? proxyProvider = null)
            : base(connectionInfo, proxyProvider ?? LightweightProxyProvider.Default, options)
        {
        }

        public override Task ConnectAsync() => this.ConnectClientAsync();

        internal abstract Task<RpcPipelineClient> ConnectClientAsync();

        protected override IRpcSerializer CreateDefaultSerializer()
        {
            return new DataContractRpcSerializer();
        }
    }
}
