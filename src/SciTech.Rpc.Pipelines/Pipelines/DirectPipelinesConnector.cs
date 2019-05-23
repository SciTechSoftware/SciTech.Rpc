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
using SciTech.Rpc.Pipelines.Client;
using SciTech.Rpc.Pipelines.Server;
using System;
using System.Collections.Generic;
using System.IO.Pipelines;

namespace SciTech.Rpc.Pipelines
{
    public class DirectPipelinesConnector
    {
        public DirectPipelinesConnector(IRpcSerializer? serializer = null, PipelinesProxyProvider? proxyProvider = null)
            : this(RpcServerId.Empty, serializer, proxyProvider)
        {
        }

        public DirectPipelinesConnector(RpcServerId serverId, IRpcSerializer? serializer = null,
            PipelinesProxyProvider? proxyProvider = null,
            IReadOnlyList<RpcClientCallInterceptor>? callInterceptors = null)
        {
            var requestPipe = new Pipe();
            var responsePipe = new Pipe();

            this.Serializer = serializer ?? new DataContractGrpcSerializer();
            this.EndPoint = new DirectPipelinesEndPoint(new DuplexPipe(requestPipe.Reader, responsePipe.Writer));

            this.Connection = new DirectPipelinesServerConnection(
                new RpcServerConnectionInfo("Direct", new Uri( "direct://localhost" ), serverId),
                new DuplexPipe(responsePipe.Reader, requestPipe.Writer),
                proxyProvider ?? PipelinesProxyProvider.Default,
                this.Serializer,
                callInterceptors);
        }

        public PipelinesServerConnection Connection { get; }

        public DirectPipelinesEndPoint EndPoint { get; }

        public IRpcSerializer Serializer { get; }

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
