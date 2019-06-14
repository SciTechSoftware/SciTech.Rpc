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
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Client
{
    public abstract class LightweightRpcConnection : RpcServerConnection
    {
        protected LightweightRpcConnection(
            RpcServerConnectionInfo connectionInfo,
            LightweightProxyProvider proxyProvider,
            IRpcSerializer serializer,
            IReadOnlyList<RpcClientCallInterceptor>? callInterceptors)
            : base(connectionInfo, proxyProvider)
        {
            this.Serializer = serializer;
            this.CallInterceptors = callInterceptors?.ToImmutableArray() ?? ImmutableArray<RpcClientCallInterceptor>.Empty;
        }

        public ImmutableArray<RpcClientCallInterceptor> CallInterceptors { get; }

        internal IRpcSerializer Serializer { get; }

        internal abstract Task<RpcPipelineClient> ConnectAsync();
    }
}
