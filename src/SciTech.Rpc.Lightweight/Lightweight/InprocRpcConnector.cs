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
using SciTech.Rpc.Lightweight.Client;
using SciTech.Rpc.Lightweight.Server;
using System;
using System.Collections.Generic;
using System.IO.Pipelines;

namespace SciTech.Rpc.Lightweight
{
    public class InprocRpcConnector
    {
        public InprocRpcConnector(IRpcClientOptions? options=null)
            : this(RpcServerId.Empty, options)
        {
        }

        public InprocRpcConnector(RpcServerId serverId, IRpcClientOptions? options = null)
        {
            var requestPipe = new Pipe(new PipeOptions(null, readerScheduler: PipeScheduler.Inline, useSynchronizationContext: false));
            var responsePipe = new Pipe(new PipeOptions(null, readerScheduler: PipeScheduler.Inline, useSynchronizationContext: false));

            this.EndPoint = new InprocRpcEndPoint(new DuplexPipe(requestPipe.Reader, responsePipe.Writer));

            this.Connection = new InprocRpcConnection(
                new RpcConnectionInfo("Direct", new Uri( "direct://localhost" ), serverId),
                new DuplexPipe(responsePipe.Reader, requestPipe.Writer),
                options);
        }

        public LightweightRpcConnection Connection { get; }

        public InprocRpcEndPoint EndPoint { get; }

        private sealed class DuplexPipe : IDuplexPipe
        {
            public DuplexPipe(PipeReader input, PipeWriter output)
            {
                this.Input = input;
                this.Output = output;
            }

            public PipeReader Input { get; }

            public PipeWriter Output { get; }
        }
    }
}
