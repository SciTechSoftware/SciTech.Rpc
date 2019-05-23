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
using SciTech.Rpc.Pipelines.Client.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace SciTech.Rpc.Pipelines.Client
{
    public abstract class PipelinesServerConnection : RpcServerConnection
    {
        protected PipelinesServerConnection(
            RpcServerConnectionInfo connectionInfo,
            PipelinesProxyProvider proxyProvider,
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
