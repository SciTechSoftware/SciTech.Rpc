﻿#region Copyright notice and license
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
    public class DirectLightweightRpcConnector
    {
        public DirectLightweightRpcConnector(IRpcSerializer? serializer = null, LightweightProxyProvider? proxyProvider = null)
            : this(RpcServerId.Empty, serializer, proxyProvider)
        {
        }

        public DirectLightweightRpcConnector(RpcServerId serverId, IRpcSerializer? serializer = null,
            LightweightProxyProvider? proxyProvider = null,
            IReadOnlyList<RpcClientCallInterceptor>? callInterceptors = null)
        {
            var requestPipe = new Pipe();
            var responsePipe = new Pipe();

            this.Serializer = serializer ?? new DataContractRpcSerializer();
            this.EndPoint = new DirectLightweightRpcEndPoint(new DuplexPipe(requestPipe.Reader, responsePipe.Writer));

            this.Connection = new DirectLightweightRpcConnection(
                new RpcServerConnectionInfo("Direct", new Uri( "direct://localhost" ), serverId),
                new DuplexPipe(responsePipe.Reader, requestPipe.Writer),
                proxyProvider ?? LightweightProxyProvider.Default,
                this.Serializer,
                callInterceptors);
        }

        public LightweightRpcConnection Connection { get; }

        public DirectLightweightRpcEndPoint EndPoint { get; }

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
