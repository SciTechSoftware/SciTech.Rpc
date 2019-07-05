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
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Client
{
    public class TcpLightweightRpcConnection : LightweightRpcConnection
    {
        private readonly object syncRoot = new object();

        private RpcPipelineClient? connectedClient;

        private volatile IDuplexPipe? connection;

        private TaskCompletionSource<RpcPipelineClient>? connectionTcs;

        private readonly SslClientOptions? sslOptions;

        private volatile SslStream? sslStream;

        private bool hasShutDown;

        public TcpLightweightRpcConnection(
            RpcServerConnectionInfo connectionInfo,
            LightweightProxyProvider proxyGenerator,
            IRpcSerializer serializer,
            SslClientOptions? sslOptions=null,            
            IReadOnlyList<RpcClientCallInterceptor>? callInterceptors = null)
            : base(connectionInfo, proxyGenerator, serializer, callInterceptors)
        {
            this.sslOptions = sslOptions;
        }

        public override Task ShutdownAsync()
        {
            TaskCompletionSource<RpcPipelineClient>? connectionTcs;
            IDuplexPipe? connection;
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

        internal override async Task<RpcPipelineClient> ConnectClientAsync()
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
            IDuplexPipe? connection = null;
            SslStream? sslStream = null;
            Stream? workStream = null;
            
            try
            {
                if (this.sslOptions != null)
                {
                    Socket? socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        SetRecommendedClientOptions(socket);

                        string sslHost;
                        switch (endPoint)
                        {
                            case IPEndPoint ipEndPoint:
                                await socket.ConnectAsync(ipEndPoint.Address, ipEndPoint.Port).ContextFree();
                                sslHost = ipEndPoint.ToString();
                                break;
                            case DnsEndPoint dnsEndPoint:
                                await socket.ConnectAsync(dnsEndPoint.Host, dnsEndPoint.Port).ContextFree();
                                sslHost = dnsEndPoint.Host;
                                break;
                            default:
                                throw new NotSupportedException($"Unsupported end point '{endPoint}',");
                        }
                        workStream = new NetworkStream(socket, true);
                        socket = null;  // Prevent closing, NetworkStream has taken ownership

                        workStream = sslStream = new SslStream(workStream, false,
                            this.sslOptions.RemoteCertificateValidationCallback,
                            this.sslOptions.LocalCertificateSelectionCallback,
                            this.sslOptions.EncryptionPolicy);

                        await sslStream.AuthenticateAsClientAsync("localhost", this.sslOptions.ClientCertificates, this.sslOptions.EnabledSslProtocols,
                            this.sslOptions.CertificateRevocationCheckMode != X509RevocationMode.NoCheck).ContextFree();

                        connection = StreamConnection.GetDuplex(sslStream);
                    }
                    finally
                    {
                        socket?.Dispose();
                    }
                }
                else
                {
                    connection = await SocketConnection.ConnectAsync(endPoint).ContextFree();
                }

                var connectedClient = new RpcPipelineClient(connection);

                TaskCompletionSource<RpcPipelineClient> connectionTcs;
                lock (this.syncRoot)
                {
                    this.connection = connection;
                    this.connectedClient = connectedClient;
                    this.sslStream = sslStream;
                    connectionTcs = this.connectionTcs;
                    this.connectionTcs = null;
                }

                connectionTcs?.SetResult(connectedClient);

                return connectedClient;
            }
            catch( Exception e )
            {
                // Connection failed, do a little cleanup
                // TODO: Try to add a unit test that cover this code (e.g. using AuthenticationException).
                // TODO: Log.

                TaskCompletionSource<RpcPipelineClient>? connectionTcs = null;
                lock (this.syncRoot)
                {
                    if (this.connectionTcs != null)
                    {
                        Debug.Assert(this.connection == null);
                        Debug.Assert(this.connectedClient == null);
                        Debug.Assert(this.sslStream == null);

                        connectionTcs = this.connectionTcs;
                        this.connectionTcs = null;
                    }
                }

                try { connectedClient?.Close(); } catch { }
                try { workStream?.Close(); } catch { }

                connectionTcs?.SetException(e);
                throw;
            }
        }

        public override bool IsConnected => this.connection != null;

        public override bool IsSigned => this.sslStream?.IsSigned ?? false;

        public override bool IsEncrypted => this.sslStream?.IsEncrypted ?? false;

        public override bool IsMutuallyAuthenticated => this.sslStream?.IsMutuallyAuthenticated ?? false;

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

        /// <summary>
        /// Set recommended socket options for client sockets
        /// </summary>
        /// <param name="socket">The socket to set options against</param>
        private static void SetRecommendedClientOptions(Socket socket)
        {
            if (socket.AddressFamily == AddressFamily.Unix) return;

            try { socket.NoDelay = true; } catch { }

            try { SetFastLoopbackOption(socket); } catch { }
        }

        private static void SetFastLoopbackOption(Socket socket)
        {
            // SIO_LOOPBACK_FAST_PATH (https://msdn.microsoft.com/en-us/library/windows/desktop/jj841212%28v=vs.85%29.aspx)
            // Speeds up localhost operations significantly. OK to apply to a socket that will not be hooked up to localhost,
            // or will be subject to WFP filtering.
            const int SIO_LOOPBACK_FAST_PATH = -1744830448;

            // windows only
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                // Win8/Server2012+ only
                var osVersion = Environment.OSVersion.Version;
                if (osVersion.Major > 6 || (osVersion.Major == 6 && osVersion.Minor >= 2))
                {
                    byte[] optionInValue = BitConverter.GetBytes(1);
                    socket.IOControl(SIO_LOOPBACK_FAST_PATH, optionInValue, null);
                }
            }
        }
    }
}
