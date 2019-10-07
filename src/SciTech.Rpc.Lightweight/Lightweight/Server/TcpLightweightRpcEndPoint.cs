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
using SciTech.Rpc.Lightweight.Server.Internal;
using SciTech.Rpc.Server;
using System;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server
{
    public class TcpLightweightRpcEndPoint : LightweightRpcEndPoint
    {
        private readonly SslServerOptions? sslOptions;

        public TcpLightweightRpcEndPoint(string hostName, int port, bool bindToAllInterfaces, SslServerOptions? sslOptions = null)
        {
            this.BindToAllInterfaces = bindToAllInterfaces;
            this.HostName = hostName ?? throw new ArgumentNullException(nameof(hostName));
            this.DisplayName = hostName;
            this.Port = port;

            this.sslOptions = sslOptions;
        }

        public bool BindToAllInterfaces { get; }

        public override string DisplayName { get; }

        public override string HostName { get; }

        public int Port { get; }

        public override RpcServerConnectionInfo GetConnectionInfo(RpcServerId hostId)
        {
            return new RpcServerConnectionInfo(this.DisplayName, new Uri($"lightweight.tcp://{this.HostName}:{this.Port}"), hostId);
        }

        protected internal override ILightweightRpcListener CreateListener(Func<IDuplexPipe, CancellationToken, Task> clientConnectedCallback, int maxRequestSize, int maxResponseSize)
        {
            ILightweightRpcListener socketServer;

            var endPoint = this.CreateNetEndPoint();

            if (this.sslOptions != null)
            {
                socketServer = new RpcSslSocketServer(endPoint, clientConnectedCallback, maxRequestSize, this.sslOptions);
            }
            else
            {
                socketServer = new RpcSocketServer(endPoint, clientConnectedCallback, maxRequestSize);
            }

            return socketServer;
        }

        private EndPoint CreateNetEndPoint()
        {
            EndPoint endPoint;

            if (this.BindToAllInterfaces)
            {
                endPoint = new IPEndPoint(IPAddress.Any, this.Port);
            }
            else if (IPAddress.TryParse(this.HostName, out var ipAddress))
            {
                endPoint = new IPEndPoint(ipAddress, this.Port);
            }
            else
            {
                // TODO: This must be improved. Or it should not be allowed to provide a DNS host name?
                var addresses = Dns.GetHostAddresses(this.HostName);
                if (addresses?.Length > 0)
                {
                    endPoint = new IPEndPoint(addresses[0], this.Port);
                }
                else
                {
                    throw new IOException($"Failed to lookup IP for '{this.HostName}'.");
                }
            }

            return endPoint;
        }

        private class RpcSocketServer : SocketServer, ILightweightRpcListener
        {
            private readonly EndPoint endPoint;

            private readonly int maxRequestSize;

            private Func<IDuplexPipe, CancellationToken, Task> clientConnectedCallback;

            /// <summary>
            /// Create a new instance of a socket server
            /// </summary>
            internal RpcSocketServer(EndPoint endPoint, Func<IDuplexPipe, CancellationToken, Task> clientConnectedCallback, int maxRequestSize)
            {
                this.clientConnectedCallback = clientConnectedCallback;
                this.endPoint = endPoint;
                this.maxRequestSize = maxRequestSize;
            }

            public void Listen()
            {
                int receivePauseThreshold = Math.Max(this.maxRequestSize, 65536);
                var receiveOptions = new PipeOptions(
                    pauseWriterThreshold: receivePauseThreshold,
                    resumeWriterThreshold: receivePauseThreshold / 2,
                    readerScheduler: PipeScheduler.Inline,
                    useSynchronizationContext: false);
                var sendOptions = new PipeOptions(
                    readerScheduler: PipeScheduler.ThreadPool,
                    useSynchronizationContext: false);
                this.Listen(this.endPoint, sendOptions: sendOptions, receiveOptions: receiveOptions);
            }

            public Task StopAsync()
            {
                this.Stop();

                return Task.CompletedTask;
            }

            protected override Task OnClientConnectedAsync(in ClientConnection client)
            {
                // TODO: Implement CancellationToken
                return this.clientConnectedCallback(client.Transport, CancellationToken.None);
            }

            protected override void OnClientFaulted(in ClientConnection client, Exception exception)
            {
                base.OnClientFaulted(client, exception);
            }

            protected override void OnServerFaulted(Exception exception)
            {
                base.OnServerFaulted(exception);
            }
        }

        private class RpcSslSocketServer : SslSocketServer, ILightweightRpcListener
        {
            private readonly Func<IDuplexPipe, CancellationToken, Task> clientConnectedCallback;

            private readonly EndPoint endPoint;

            private readonly int maxRequestSize;

            /// <summary>
            /// Create a new instance of a socket server
            /// </summary>
            internal RpcSslSocketServer(EndPoint endPoint, Func<IDuplexPipe, CancellationToken, Task> clientConnectedCallback, int maxRequestSize, SslServerOptions? sslOptions = null)
                : base(sslOptions)
            {
                this.clientConnectedCallback = clientConnectedCallback;
                this.endPoint = endPoint;
                this.maxRequestSize = maxRequestSize;
            }

            public void Dispose()
            {
                this.Stop();
            }

            public void Listen()
            {
                int receivePauseThreshold = Math.Max(this.maxRequestSize, 65536);
                var receiveOptions = new PipeOptions(pauseWriterThreshold: receivePauseThreshold, resumeWriterThreshold: receivePauseThreshold / 2, useSynchronizationContext: false);
                this.Listen(this.endPoint, receiveOptions: receiveOptions);
            }

            public Task StopAsync()
            {
                this.Stop();

                return Task.CompletedTask;
            }

            protected override Task OnClientConnectedAsync(in ClientConnection client)
            {
                // TODO: Implement CancellationToken
                return this.clientConnectedCallback(client.Transport, CancellationToken.None);
            }

            protected override void OnClientFaulted(in ClientConnection client, Exception exception)
            {
                base.OnClientFaulted(client, exception);
            }

            protected override void OnServerFaulted(Exception exception)
            {
                base.OnServerFaulted(exception);
            }
        }
    }
}
