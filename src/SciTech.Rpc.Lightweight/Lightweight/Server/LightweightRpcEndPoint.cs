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
using SciTech.Rpc.Server;
using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server
{
    public abstract class LightweightRpcEndPoint : IRpcServerEndPoint
    {
        public abstract string DisplayName { get; }

        public abstract string HostName { get; }

        public abstract RpcServerConnectionInfo GetConnectionInfo(RpcServerId serverId);

        protected internal abstract ILightweightRpcListener CreateListener(Func<IDuplexPipe, CancellationToken, Task> clientConnectedCallback, int maxRequestSize, int maxResponseSize);
    }
}
