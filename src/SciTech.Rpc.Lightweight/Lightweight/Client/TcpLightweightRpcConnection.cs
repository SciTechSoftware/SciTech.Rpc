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
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Logging;
using SciTech.Threading;
using System;
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
        // TODO: Add logging.
        //private static readonly ILog Logger = LogProvider.For<TcpLightweightRpcConnection>();

        private readonly SslClientOptions? sslOptions;

        private readonly object syncRoot = new object();

        private RpcPipelineClient? connectedClient;

        private TaskCompletionSource<RpcPipelineClient>? connectionTcs;

        private volatile SslStream? sslStream;

        public TcpLightweightRpcConnection(
            RpcServerConnectionInfo connectionInfo,
            SslClientOptions? sslOptions = null,
            ImmutableRpcClientOptions? options = null,
            LightweightProxyProvider? proxyProvider = null,
            LightweightOptions? lightweightOptions = null)
            : base(connectionInfo, options, proxyProvider, lightweightOptions)
        {
            this.sslOptions = sslOptions;
        }

        public override bool IsConnected => this.connectedClient != null;

        public override bool IsEncrypted => this.sslStream?.IsEncrypted ?? false;

        public override bool IsMutuallyAuthenticated => this.sslStream?.IsMutuallyAuthenticated ?? false;

        public override bool IsSigned => this.sslStream?.IsSigned ?? false;

        public override async Task ShutdownAsync()
        {
            var prevClient = this.ResetConnection(RpcConnectionState.Disconnected, null);

            if (prevClient != null)
            {
                await prevClient.AwaitFinished().ContextFree();
            }

            this.NotifyDisconnected();
        }

        internal override ValueTask<RpcPipelineClient> ConnectClientAsync()
        {
            Task<RpcPipelineClient>? activeConnectionTask = null;
            lock (this.syncRoot)
            {
                if (this.connectedClient != null)
                {
                    return new ValueTask<RpcPipelineClient>(this.connectedClient);
                }

                if (this.ConnectionState == RpcConnectionState.Disconnected)
                {
                    throw new ObjectDisposedException(this.ToString());
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
                async ValueTask<RpcPipelineClient> AwaitConnection()
                {
                    return await activeConnectionTask.ContextFree();
                }

                return AwaitConnection();
            }

            async ValueTask<RpcPipelineClient> DoConnect()
            {
                var endPoint = this.CreateNetEndPoint();
                IDuplexPipe? connection = null;
                SslStream? sslStream = null;
                Stream? workStream = null;

                int sendMaxMessageSize = this.Options?.SendMaxMessageSize ?? DefaultMaxRequestMessageSize;
                int receiveMaxMessageSize = this.Options?.ReceiveMaxMessageSize ?? DefaultMaxResponseMessageSize;

                var sendOptions = new PipeOptions(
                    pauseWriterThreshold: sendMaxMessageSize * 2, resumeWriterThreshold: sendMaxMessageSize,
                    useSynchronizationContext: false);
                var receiveOptions = new PipeOptions(
                    pauseWriterThreshold: receiveMaxMessageSize * 2, resumeWriterThreshold: receiveMaxMessageSize,
                    useSynchronizationContext: false);

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
                                    sslHost = ipEndPoint.Address.ToString();
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

                            await sslStream.AuthenticateAsClientAsync(sslHost, this.sslOptions.ClientCertificates, this.sslOptions.EnabledSslProtocols,
                                this.sslOptions.CertificateRevocationCheckMode != X509RevocationMode.NoCheck).ContextFree();


                            connection = StreamConnection.GetDuplex(sslStream, sendOptions, receiveOptions);
                            if (!(connection is IDisposable))
                            {
#pragma warning disable CA2000 // Dispose objects before losing scope
                                // Rather dummy, we need to dispose the stream when pipe is disposed, but
                                // this is not performed by the pipe returned by StreamConnection.
                                connection = new OwnerDuplexPipe(connection, sslStream);
#pragma warning restore CA2000 // Dispose objects before losing scope
                            }
                        }
                        finally
                        {
                            socket?.Dispose();
                        }
                    }
                    else
                    {
                        connection = await SocketConnection.ConnectAsync(endPoint, sendOptions, receiveOptions).ContextFree();
                        Debug.Assert(connection is IDisposable);
                    }

                    var connectedClient = new RpcPipelineClient(connection, sendMaxMessageSize, receiveMaxMessageSize, this.KeepSizeLimitedConnectionAlive);
                    connectedClient.ReceiveLoopFaulted += this.ConnectedClient_ReceiveLoopFaulted;
                    connectedClient.RunAsyncCore();

                    TaskCompletionSource<RpcPipelineClient>? connectionTcs;
                    lock (this.syncRoot)
                    {
                        this.connectedClient = connectedClient;
                        this.sslStream = sslStream;
                        connectionTcs = this.connectionTcs;
                        this.connectionTcs = null;
                        workStream = null;
                        this.SetConnectionState(RpcConnectionState.Connected);
                    }

                    connectionTcs?.SetResult(connectedClient);

                    this.NotifyConnected();

                    return connectedClient;
                }
                catch (Exception e)
                {
                    // Connection failed, do a little cleanup
                    // TODO: Try to add a unit test that cover this code (e.g. using AuthenticationException).
                    // TODO: Log.

                    TaskCompletionSource<RpcPipelineClient>? connectionTcs = null;
                    lock (this.syncRoot)
                    {
                        if (this.connectionTcs != null)
                        {
                            Debug.Assert(this.connectedClient == null);
                            Debug.Assert(this.sslStream == null);

                            connectionTcs = this.connectionTcs;
                            this.connectionTcs = null;
                        }

                        this.SetConnectionState(RpcConnectionState.ConnectionFailed);
                    }

                    workStream?.Dispose();

                    connectionTcs?.SetException(e);

                    this.NotifyConnectionFailed();

                    throw;
                }
            }

            return DoConnect();
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

        /// <summary>
        /// Set recommended socket options for client sockets
        /// </summary>
        /// <param name="socket">The socket to set options against</param>
#pragma warning disable CA1031 // Do not catch general exception types
        private static void SetRecommendedClientOptions(Socket socket)
        {
            if (socket.AddressFamily == AddressFamily.Unix) return;

            try { socket.NoDelay = true; } catch { }

            try { SetFastLoopbackOption(socket); } catch { }
        }

#pragma warning restore CA1031 // Do not catch general exception types


        private void ConnectedClient_ReceiveLoopFaulted(object sender, ExceptionEventArgs e)
        {
            this.ResetConnection(RpcConnectionState.ConnectionLost, e.Exception);

            this.NotifyConnectionLost();
        }

#pragma warning restore CA1031 // Do not catch general exception types

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

        private RpcPipelineClient? ResetConnection(RpcConnectionState state, Exception? ex)
        {
            TaskCompletionSource<RpcPipelineClient>? connectionTcs;
            RpcPipelineClient? connectedClient;
            lock (this.syncRoot)
            {
                if (this.ConnectionState == RpcConnectionState.Disconnected)
                {
                    // Already disconnected
                    Debug.Assert(this.connectedClient == null);
                    return null;
                }

                connectedClient = this.connectedClient;
                this.connectedClient = null;
                this.sslStream = null;
                connectionTcs = this.connectionTcs;
                this.connectionTcs = null;

                this.SetConnectionState(state);
            }

            connectionTcs?.TrySetCanceled();

            // TODO: wait for unfinished frames?
            if (connectedClient != null)
            {
                connectedClient.ReceiveLoopFaulted -= this.ConnectedClient_ReceiveLoopFaulted;
                connectedClient.Close(ex);
            }

            return connectedClient;
        }
    }
}
