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
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server
{
    public class TcpRpcEndPoint : LightweightRpcEndPoint
    {
        private readonly SslServerOptions? sslOptions;

        private readonly NegotiateServerOptions? negotiateOptions;

        public TcpRpcEndPoint(string hostName, int port, bool bindToAllInterfaces, NegotiateServerOptions? negotiateOptions )
            : this(hostName, CreateNetEndPoint(hostName, port, bindToAllInterfaces), negotiateOptions)
        {
        }

        public TcpRpcEndPoint(string hostName, string endPointAddress, int port, bool bindToAllInterfaces, NegotiateServerOptions? negotiateOptions )
            : this(hostName, CreateNetEndPoint(endPointAddress, port, bindToAllInterfaces), negotiateOptions)
        {
        }

        public TcpRpcEndPoint(string hostName, IPEndPoint endPoint, NegotiateServerOptions? negotiateOptions )
        {
            this.HostName = hostName ?? throw new ArgumentNullException(nameof(hostName));
            this.DisplayName = hostName;
            this.EndPoint = endPoint;

            this.negotiateOptions = negotiateOptions;
        }

        public TcpRpcEndPoint(string hostName, int port, bool bindToAllInterfaces, SslServerOptions? sslOptions = null)
            : this( hostName, CreateNetEndPoint(hostName, port, bindToAllInterfaces ), sslOptions )
        {
        }

        public TcpRpcEndPoint(string hostName, string endPointAddress, int port, bool bindToAllInterfaces, SslServerOptions? sslOptions = null)
            : this(hostName, CreateNetEndPoint(endPointAddress, port, bindToAllInterfaces), sslOptions )
        {
        }

        public TcpRpcEndPoint(string hostName, IPEndPoint endPoint, SslServerOptions? sslOptions = null)
        {
            this.HostName = hostName ?? throw new ArgumentNullException(nameof(hostName));
            this.DisplayName = hostName;
            this.EndPoint = endPoint;

            this.sslOptions = sslOptions;
        }

        public override string DisplayName { get; }

        public override string HostName { get; }

        public IPEndPoint EndPoint { get; }

        public int Port => this.EndPoint.Port;

        public override RpcServerConnectionInfo GetConnectionInfo(RpcServerId serverId)
        {
            return new RpcServerConnectionInfo(this.DisplayName, new Uri($"lightweight.tcp://{this.HostName}:{this.Port}"), serverId);
        }

        protected internal override ILightweightRpcListener CreateListener(            
            IRpcConnectionHandler connectionHandler,
            int maxRequestSize, int maxResponseSize)
        {
            ILightweightRpcListener socketServer;

            if (this.sslOptions != null || this.negotiateOptions != null )
            {
                socketServer = new RpcSslSocketServer(this, connectionHandler, maxRequestSize, this.sslOptions, this.negotiateOptions);
            }
            else
            {
                socketServer = new RpcSocketServer(this, connectionHandler, maxRequestSize);
            }

            return socketServer;
        }

        private static IPEndPoint CreateNetEndPoint(string serverAddress, int port, bool bindToAllInterfaces )
        {
            if (bindToAllInterfaces)
            {
                return new IPEndPoint(IPAddress.Any, port);
            }
            else if (IPAddress.TryParse(serverAddress, out var ipAddress))
            {
                return new IPEndPoint(ipAddress, port);
            }
            else
            {
                // TODO: Should this really be allowed? If there's more than one matching address 
                // it's not possible to know which one is selected.
                var addresses = Dns.GetHostAddresses(serverAddress);
                
                if (addresses?.Length > 0)
                {
                    var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                    foreach( var address in addresses )
                    {
                        var ni = networkInterfaces.FirstOrDefault(i => i.GetIPProperties().UnicastAddresses.Any(a => Equals(a.Address, address)));
                        if( ni?.OperationalStatus == OperationalStatus.Up )
                        {
                            // This interface looks usable. (But maybe we should see if there's more than one usable interface?)
                            return new IPEndPoint(address, port);
                        }
                    }
                    
                }

                throw new IOException($"Failed to lookup IP address for '{serverAddress}'.");
            }
        }

        private class RpcSocketServer : SocketServer, ILightweightRpcListener
        {
            private readonly TcpRpcEndPoint rpcEndPoint;

            private readonly int maxRequestSize;

            private readonly IRpcConnectionHandler connectionHandler;

            /// <summary>
            /// Create a new instance of a socket server
            /// </summary>
            internal RpcSocketServer(TcpRpcEndPoint rpcEndPoint, IRpcConnectionHandler connectionHandler, int maxRequestSize)
            {
                this.connectionHandler = connectionHandler;
                this.rpcEndPoint = rpcEndPoint;
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
                this.Listen(this.rpcEndPoint.EndPoint, sendOptions: sendOptions, receiveOptions: receiveOptions);
            }

            public Task StopAsync()
            {
                this.Stop();

                return Task.CompletedTask;
            }

            protected override Task OnClientConnectedAsync(in ClientConnection client)
            {
                // TODO: Implement CancellationToken
                return this.connectionHandler.RunPipelineClientAsync(client.Transport, this.rpcEndPoint, null, CancellationToken.None);
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
            private readonly IRpcConnectionHandler connectionHandler;

            private readonly TcpRpcEndPoint rpcEndPoint;

            private readonly int maxRequestSize;

            /// <summary>
            /// Create a new instance of a socket server
            /// </summary>
            internal RpcSslSocketServer(TcpRpcEndPoint rpcEndPoint, IRpcConnectionHandler connectionHandler, int maxRequestSize, SslServerOptions? sslOptions, NegotiateServerOptions? negotiateOptions)
                : base(sslOptions, negotiateOptions)
            {
                this.connectionHandler = connectionHandler;
                this.rpcEndPoint = rpcEndPoint;
                this.maxRequestSize = maxRequestSize;
            }

            public void Dispose()
            {
                this.Stop();
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

                this.Listen(this.rpcEndPoint.EndPoint, sendOptions: sendOptions, receiveOptions: receiveOptions);
            }

            public Task StopAsync()
            {
                this.Stop();

                return Task.CompletedTask;
            }

            protected override Task OnClientConnectedAsync(in ClientConnection client)
            {
                // TODO: Implement CancellationToken
                return this.connectionHandler.RunPipelineClientAsync(client.Transport, this.rpcEndPoint, client.User, CancellationToken.None);
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
