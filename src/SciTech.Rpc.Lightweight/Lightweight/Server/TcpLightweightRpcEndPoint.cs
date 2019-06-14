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
using System;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server
{
    public class TcpLightweightRpcEndPoint : ILightweightRpcEndPoint
    {
        private RpcSocketServer? socketServer;

        private object syncRoot = new object();

        public TcpLightweightRpcEndPoint(string hostName, int port, bool bindToAllInterfaces)
        {
            this.BindToAllInterfaces = bindToAllInterfaces;
            this.HostName = hostName ?? throw new ArgumentNullException(nameof(hostName));
            this.DisplayName = hostName;
            this.Port = port;
        }

        public bool BindToAllInterfaces { get; }

        public string DisplayName { get; }

        public string HostName { get; }

        public int Port { get; }

        public RpcServerConnectionInfo GetConnectionInfo(RpcServerId hostId)
        {
            return new RpcServerConnectionInfo(this.DisplayName, new Uri( $"lightweight.tcp://{this.HostName}:{this.Port}" ), hostId);
        }

        public void Start(Func<IDuplexPipe, Task> clientConnectedCallback)
        {
            RpcSocketServer socketServer;

            lock (this.syncRoot)
            {
                if (this.socketServer != null)
                {
                    throw new InvalidOperationException("TcpLightweightRpcEndPoint already started.");
                }

                this.socketServer = socketServer = new RpcSocketServer(clientConnectedCallback);
            }

            var endPoint = this.CreateNetEndPoint();
            socketServer.Listen(endPoint);
        }

        public Task StopAsync()
        {
            RpcSocketServer? socketServer;
            lock (this.syncRoot)
            {
                socketServer = this.socketServer;
                this.socketServer = null;
            }

            socketServer?.Stop();

            return Task.CompletedTask;
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
                // TODO: This must be improved.
                var addresses = Dns.GetHostAddresses(this.HostName);
                if (addresses?.Length > 0)
                {
                    endPoint = new IPEndPoint(addresses[0], this.Port);
                } else
                {
                    throw new IOException($"Failed to lookup IP for '{this.HostName}'.");
                }
            }

            return endPoint;
        }

        private class RpcSocketServer : SocketServer
        {
            private Func<IDuplexPipe, Task> clientConnectedCallback;

            /// <summary>
            /// Create a new instance of a socket server
            /// </summary>
            internal RpcSocketServer(Func<IDuplexPipe, Task> clientConnectedCallback)
            {
                this.clientConnectedCallback = clientConnectedCallback;
            }

            protected override Task OnClientConnectedAsync(in ClientConnection client)
            {
                return this.clientConnectedCallback(client.Transport);
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
