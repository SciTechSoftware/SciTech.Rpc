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

using Pipelines.Sockets.Unofficial;
using SciTech.Rpc.Client;
using SciTech.Rpc.Lightweight.Client.Internal;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Threading;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Client
{
    public class TcpRpcConnection : LightweightRpcConnection
    {
        // TODO: Add logging.
        //private static readonly ILog Logger = LogProvider.For<TcpLightweightRpcConnection>();

        private AuthenticationClientOptions? authenticationOptions;

        private volatile AuthenticatedStream? authenticatedStream;

        public TcpRpcConnection(
            RpcServerConnectionInfo connectionInfo,
            AuthenticationClientOptions? authenticationOptions = null,
            IRpcClientOptions? options = null,
            LightweightOptions? lightweightOptions = null)
            : this(connectionInfo, authenticationOptions, options,
                  LightweightProxyGenerator.Default,
                  lightweightOptions)
        {
        }



        internal TcpRpcConnection(
            RpcServerConnectionInfo connectionInfo,
            AuthenticationClientOptions? authenticationOptions,
            IRpcClientOptions? options,
            LightweightProxyGenerator proxyGenerator,
            LightweightOptions? lightweightOptions)
            : base(connectionInfo, options,
                  proxyGenerator,
                  lightweightOptions)
        {
            var scheme = this.ConnectionInfo.HostUrl?.Scheme;
            switch( scheme )
            {
                case WellKnownRpcSchemes.LightweightTcp:
                    break;
                default:
                    throw new ArgumentException("Invalid connectionInfo scheme.", nameof(connectionInfo));
            }

            if( authenticationOptions != null )
            {
                if( authenticationOptions is SslClientOptions || authenticationOptions is NegotiateClientOptions)
                {
                    this.authenticationOptions = authenticationOptions;
                } else
                {
                    throw new ArgumentException("Authentication options not supported.", nameof(authenticationOptions));
                }
            }
        }


        public override bool IsEncrypted => this.authenticatedStream?.IsEncrypted ?? false;

        public override bool IsMutuallyAuthenticated => this.authenticatedStream?.IsMutuallyAuthenticated ?? false;

        public override bool IsSigned => this.authenticatedStream?.IsSigned ?? false;

        [EditorBrowsable(EditorBrowsableState.Never)]
        protected override async Task<IDuplexPipe> ConnectPipelineAsync(int sendMaxMessageSize, int receiveMaxMessageSize, CancellationToken cancellationToken)
        {
            // TODO: Implement cancellationToken somehow, but how?. ConnectAsync and AuthenticateAsClientAsync don't accept a CancellationToken.
            IDuplexPipe? connection;

            var endPoint = this.CreateNetEndPoint();
            AuthenticatedStream? authenticatedStream = null;
            Stream? workStream = null;

            var sendOptions = new PipeOptions(
                pauseWriterThreshold: sendMaxMessageSize * 2, resumeWriterThreshold: sendMaxMessageSize,
                readerScheduler: PipeScheduler.ThreadPool,
                useSynchronizationContext: false);
            var receiveOptions = new PipeOptions(
                pauseWriterThreshold: receiveMaxMessageSize * 2, resumeWriterThreshold: receiveMaxMessageSize,
                readerScheduler: PipeScheduler.Inline,
                useSynchronizationContext: false);

            try
            {
                if (this.authenticationOptions != null )
                {
                    Socket? socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        SetRecommendedClientOptions(socket);

                        string sslHost;
                        switch (endPoint)
                        {
                            case IPEndPoint ipEndPoint:
#if PLAT_CONNECT_CANCELLATION
                                await socket.ConnectAsync(ipEndPoint.Address, ipEndPoint.Port,cancellationToken).ContextFree();
#else
                                await socket.ConnectAsync(ipEndPoint.Address, ipEndPoint.Port).ContextFree();
#endif
                                sslHost = ipEndPoint.Address.ToString();
                                break;
                            case DnsEndPoint dnsEndPoint:
#if PLAT_CONNECT_CANCELLATION
                                await socket.ConnectAsync(dnsEndPoint.Host, dnsEndPoint.Port, cancellationToken).ContextFree();
#else
                                await socket.ConnectAsync(dnsEndPoint.Host, dnsEndPoint.Port).ContextFree();
#endif

                                sslHost = dnsEndPoint.Host;
                                break;
                            default:
                                throw new NotSupportedException($"Unsupported end point '{endPoint}',");
                        }
                        workStream = new NetworkStream(socket, true);
                        socket = null;  // Prevent closing, NetworkStream has taken ownership

                        if (this.authenticationOptions is SslClientOptions sslOptions)
                        {
                            var sslStream = new SslStream(workStream, false,
                                sslOptions.RemoteCertificateValidationCallback,
                                sslOptions.LocalCertificateSelectionCallback,
                                sslOptions.EncryptionPolicy);

                            workStream = authenticatedStream = sslStream;

#if PLAT_CONNECT_CANCELLATION
                            var authOptions = new SslClientAuthenticationOptions()
                            {
                                TargetHost = sslHost,
                                ClientCertificates = sslOptions.ClientCertificates,
                                EnabledSslProtocols=sslOptions.EnabledSslProtocols,
                                CertificateRevocationCheckMode = sslOptions.CertificateRevocationCheckMode
                            };

                            await sslStream.AuthenticateAsClientAsync(authOptions, cancellationToken).ContextFree();
#else
                            await sslStream.AuthenticateAsClientAsync(sslHost, sslOptions.ClientCertificates, sslOptions.EnabledSslProtocols,
                                sslOptions.CertificateRevocationCheckMode != X509RevocationMode.NoCheck).ContextFree();
#endif
                        }
                        else if( this.authenticationOptions is NegotiateClientOptions negotiateOptions)
                        {
                            var negotiateStream = new NegotiateStream(workStream, false);
                            workStream = authenticatedStream = negotiateStream;

                            await negotiateStream.AuthenticateAsClientAsync(
                                negotiateOptions!.Credential ?? CredentialCache.DefaultNetworkCredentials, 
                                negotiateOptions.TargetName ?? "").ContextFree();
                        } else
                        {
                            throw new NotSupportedException("Authentication options not supported.");
                        }

                        connection = StreamConnection.GetDuplex(authenticatedStream, sendOptions, receiveOptions);
                        if (!(connection is IDisposable))
                        {
#pragma warning disable CA2000 // Dispose objects before losing scope
                            // Rather dummy, we need to dispose the stream when pipe is disposed, but
                            // this is not performed by the pipe returned by StreamConnection.
                            connection = new OwnerDuplexPipe(connection, authenticatedStream);
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

                this.authenticatedStream = authenticatedStream;
                workStream = null;

                return connection;
            }
            finally
            {
                workStream?.Dispose();
            }
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        protected override void OnConnectionResetSynchronized()
        {
            base.OnConnectionResetSynchronized();
            this.authenticatedStream = null;
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
        private static void SetRecommendedClientOptions(Socket socket)
        {
#pragma warning disable CA1031 // Do not catch general exception types
            if (socket.AddressFamily == AddressFamily.Unix) return;

            try { socket.NoDelay = true; } catch { }

            try { SetFastLoopbackOption(socket); } catch { }
#pragma warning restore CA1031 // Do not catch general exception types
        }

        private EndPoint CreateNetEndPoint()
        {
            // TODO: The URL should be parsed in RpConnectionInfo constructor .
            // If invalid an ArgumentException should be thrown there.
            EndPoint endPoint;

            if (this.ConnectionInfo.HostUrl is Uri uri)
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
                    throw new InvalidOperationException($"Failed to parse HostUrl '{uri}'.", e);
                }
            }
            else
            {
                throw new InvalidOperationException($"Missing HostUrl '{this.ConnectionInfo.HostUrl}'.");
            }
        }
    }
}

