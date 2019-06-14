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

using Pipelines.Sockets.Unofficial;
using SciTech.Rpc.Client;
using SciTech.Rpc.Lightweight.Client.Internal;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Client
{
    public class TcpLightweightRpcConnection : LightweightRpcConnection
    {
        private readonly object syncRoot = new object();

        private RpcPipelineClient? connectedClient;

        private SocketConnection? connection;

        private TaskCompletionSource<RpcPipelineClient>? connectionTcs;

        private bool hasShutDown;

        public TcpLightweightRpcConnection(
            RpcServerConnectionInfo connectionInfo,
            LightweightProxyProvider proxyGenerator,
            IRpcSerializer serializer,
            IReadOnlyList<RpcClientCallInterceptor>? callInterceptors = null)
            : base(connectionInfo, proxyGenerator, serializer, callInterceptors)
        {
        }

        public override Task ShutdownAsync()
        {
            TaskCompletionSource<RpcPipelineClient>? connectionTcs;
            SocketConnection? connection;
            RpcPipelineClient? connectedClient;
            lock (this.syncRoot)
            {
                connection = this.connection;
                this.connection = null;
                connectedClient = this.connectedClient;
                this.connectedClient = null;
                connectionTcs = this.connectionTcs;
                this.connectionTcs = null;
                this.hasShutDown = true;
            }

            connectionTcs?.SetCanceled();

            // TODO: wait for unfinished frames?
            if (connectedClient != null)
            {
                connectedClient.Close();
                return connectedClient.AwaitFinished();
            }

            return Task.CompletedTask;
        }

        internal override async Task<RpcPipelineClient> ConnectAsync()
        {
            Task<RpcPipelineClient>? activeConnectionTask = null;
            lock (this.syncRoot)
            {
                if (this.hasShutDown)
                {
                    throw new ObjectDisposedException(this.ToString());
                }
                if (this.connectedClient != null)
                {
                    return this.connectedClient;
                }

                if (this.connectionTcs != null)
                {
                    activeConnectionTask = this.connectionTcs.Task;
                }
                else
                {
                    this.connectionTcs = new TaskCompletionSource<RpcPipelineClient>();
                }
            }

            if (activeConnectionTask != null)
            {
                return await this.connectionTcs.Task.ContextFree();
            }


            var endPoint = this.CreateNetEndPoint();
            var connection = await SocketConnection.ConnectAsync(endPoint).ContextFree();

            var connectedClient = new RpcPipelineClient(connection);

            TaskCompletionSource<RpcPipelineClient> connectionTcs;
            lock (this.syncRoot)
            {
                this.connection = connection;
                this.connectedClient = connectedClient;
                connectionTcs = this.connectionTcs;
                this.connectionTcs = null;
            }

            connectionTcs?.SetResult(connectedClient);

            return connectedClient;
        }

        private EndPoint CreateNetEndPoint()
        {
            // TODO: The URL should be parsed in RpConnectionInfo constructor .
            // If invalid an ArgumentException should be thrown there.
            EndPoint endPoint;

            if (Uri.TryCreate(this.ConnectionInfo.HostUrl, UriKind.Absolute, out var uri))
            {
                try
                {
                    if (uri.HostNameType != UriHostNameType.Dns && IPAddress.TryParse(uri.Host, out var ipAddress))
                    {
                        endPoint = new IPEndPoint(ipAddress, uri.Port);
                    }
                    else
                    {
                        endPoint = new DnsEndPoint(uri.DnsSafeHost, uri.Port);
                    }

                    return endPoint;
                }
                catch (ArgumentException e)
                {
                    throw new InvalidOperationException($"Failed to parse HostUrl '{this.ConnectionInfo.HostUrl}'.", e);
                }
            }
            else
            {
                throw new InvalidOperationException($"Invalid HostUrl '{this.ConnectionInfo.HostUrl}'.");
            }
        }
    }
}
