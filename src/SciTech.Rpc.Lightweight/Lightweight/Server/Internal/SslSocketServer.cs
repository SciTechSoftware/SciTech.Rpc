#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//

// Based on Pipelines.Sockets.Unofficial.SocketServer (https://github.com/mgravell/Pipelines.Sockets.Unofficial)
//
// Copyright (c) 2018 Marc Gravell
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.
#endregion

using Pipelines.Sockets.Unofficial;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Server;
using SciTech.Threading;
using System;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server.Internal
{
    /// <summary>
    /// Modified copy of <see cref="SocketServer"/> with added support for authentication through <see cref="SslStream"/> and <see cref="NegotiateStream"/>.
    /// </summary>
    internal abstract class SslSocketServer
    {
        private readonly Action<object?> RunClientAsync;

        private readonly AuthenticationServerOptions? authenticationOptions;

        private Socket? listener;

#pragma warning disable CA1031 // Do not catch general exception types
        protected SslSocketServer(AuthenticationServerOptions? authenticationOptions)
        {
            this.authenticationOptions = authenticationOptions;

            this.RunClientAsync = async boxed =>
            {
                if (boxed is ClientConnection client)
                {
                    try
                    {
                        await this.OnClientConnectedAsync(client).ContextFree();
                        try { client.Transport.Input.Complete(); } catch { }
                        try { client.Transport.Output.Complete(); } catch { }
                    }
                    catch (Exception ex)
                    {
                        try { client.Transport.Input.Complete(ex); } catch { }
                        try { client.Transport.Output.Complete(ex); } catch { }
                        this.OnClientFaulted(in client, ex);
                    }
                    finally
                    {
                        if (client.Transport is IDisposable d)
                        {
                            try { d.Dispose(); } catch { }
                        }
                    }
                }
            };
        }
#pragma warning restore CA1031 // Do not catch general exception types


        /// <summary>
        /// Start listening as a server
        /// </summary>
        public void Listen(EndPoint endPoint,
            AddressFamily? addressFamily = null,
            SocketType socketType = SocketType.Stream,
            ProtocolType protocolType = ProtocolType.Tcp,
            int listenBacklog = 20,
            PipeOptions? sendOptions = null, PipeOptions? receiveOptions = null)
        {
            if (this.listener != null) throw new InvalidOperationException("Server is already running");
            Socket listener = new Socket(addressFamily ?? endPoint.AddressFamily, socketType, protocolType);
            listener.Bind(endPoint);
            listener.Listen(listenBacklog);

            this.listener = listener;
            StartOnScheduler(receiveOptions?.ReaderScheduler, _ => this.ListenForConnectionsAsync(
                sendOptions ?? PipeOptions.Default, receiveOptions ?? PipeOptions.Default).Forget(), null);

            this.OnStarted(endPoint);
        }

#pragma warning disable CA1031 // Do not catch general exception types
        public void Stop()
        {
            var socket = this.listener;
            this.listener = null;
            if (socket != null)
            {
                try { socket.Dispose(); } catch { }
            }
        }

#pragma warning restore CA1031 // Do not catch general exception types

        /// <summary>
        /// Invoked when a new client connects
        /// </summary>
        protected abstract Task OnClientConnectedAsync(in ClientConnection client);

        /// <summary>
        /// Invoked when a client has faulted
        /// </summary>
        protected virtual void OnClientFaulted(in ClientConnection client, Exception exception) { }

        /// <summary>
        /// Invoked when the server has faulted
        /// </summary>
        protected virtual void OnServerFaulted(Exception exception) { }

        /// <summary>
        /// Invoked when the server starts
        /// </summary>
        protected virtual void OnStarted(EndPoint endPoint) { }

        private static void StartOnScheduler(PipeScheduler? scheduler, Action<object?> callback, object? state)
        {
            if (scheduler == PipeScheduler.Inline) scheduler = null;
            (scheduler ?? PipeScheduler.ThreadPool).Schedule(callback, state);
        }

#pragma warning disable CA1031 // Do not catch general exception types
#pragma warning disable CA2000 // Dispose objects before losing scope
        private async Task ListenForConnectionsAsync(PipeOptions sendOptions, PipeOptions receiveOptions)
        {
            try
            {
                while (true)
                {
                    var listener = this.listener;
                    if (listener == null) break;

                    var clientSocket = await listener.AcceptAsync().ContextFree();
                    var remoteEndPoint = clientSocket.RemoteEndPoint;
                    if (remoteEndPoint != null)
                    {
                        SocketConnection.SetRecommendedServerOptions(clientSocket);

                        Stream socketStream = new NetworkStream(clientSocket, true);
                        IPrincipal? user = null;

                        if (this.authenticationOptions is SslServerOptions sslOptions)
                        {
                            if (sslOptions.ServerCertificate != null)
                            {
                                var sslStream = new SslStream(socketStream);
                                await sslStream.AuthenticateAsServerAsync(sslOptions.ServerCertificate,
                                    sslOptions.ClientCertificateRequired,
                                    sslOptions.EnabledSslProtocols,
                                    sslOptions.CertificateRevocationCheckMode != X509RevocationMode.NoCheck).ContextFree();

                                socketStream = sslStream;
                            }
                        } else if ( this.authenticationOptions is NegotiateServerOptions negotiateOptions)
                        {
                            var negotiateStream = new NegotiateStream(socketStream);
                            try
                            {
                                await negotiateStream.AuthenticateAsServerAsync(
                                    negotiateOptions.Credential ?? CredentialCache.DefaultNetworkCredentials, 
                                    ProtectionLevel.EncryptAndSign, TokenImpersonationLevel.Identification).ContextFree();
                                user = CreatePrincipal(negotiateStream.RemoteIdentity);
                            }
                            catch
                            {
                                try { negotiateStream.Dispose(); } catch { }
                                throw;
                            }
                            
                            socketStream = negotiateStream;
                        }

                        var pipe = StreamConnection.GetDuplex(socketStream, sendOptions, receiveOptions);
                        if (!(pipe is IDisposable))
                        {
                            // Rather dummy, we need to dispose the stream when pipe is disposed, but
                            // this is not performed by the pipe returned by StreamConnection.
                            pipe = new OwnerDuplexPipe(pipe, socketStream);
                        }

                        StartOnScheduler((receiveOptions ?? PipeOptions.Default).ReaderScheduler, this.RunClientAsync,
                            new ClientConnection(pipe, remoteEndPoint, user)); // boxed, but only once per client
                    }
                }
            }
            catch (NullReferenceException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { this.OnServerFaulted(ex); }
        }
#pragma warning restore CA1031 // Do not catch general exception types
#pragma warning restore CA2000 // Dispose objects before losing scope

        private static IPrincipal? CreatePrincipal(IIdentity? identity)
        {
            return identity switch
            {
                null => null,
                WindowsIdentity windowsIdentity => new WindowsPrincipal(windowsIdentity),
                ClaimsIdentity claimsIdentity => new ClaimsPrincipal(claimsIdentity),
                _ => new GenericPrincipal(identity, null),
            };
        }

        /// <summary>
        /// The state of a client connection
        /// </summary>
        protected readonly struct ClientConnection
        {
            internal ClientConnection(IDuplexPipe transport, EndPoint remoteEndPoint, IPrincipal? user)
            {
                this.Transport = transport;
                this.RemoteEndPoint = remoteEndPoint;
                this.User = user;
            }

            /// <summary>
            /// The remote endpoint that the client connected from
            /// </summary>
            public EndPoint RemoteEndPoint { get; }

            /// <summary>
            /// The transport to use for this connection
            /// </summary>
            public IDuplexPipe Transport { get; }

            public IPrincipal? User { get; }
        }
    }
}
