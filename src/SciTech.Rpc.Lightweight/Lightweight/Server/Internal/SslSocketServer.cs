﻿#region Copyright notice and license
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

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pipelines.Sockets.Unofficial;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Server;
using SciTech.Threading;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server.Internal
{
    /// <summary>
    /// Modified copy of <see cref="SocketServer"/> with added support for authentication through <see cref="SslStream"/> and <see cref="NegotiateStream"/>.
    /// </summary>
    internal abstract class SslSocketServer
    {
        private readonly ILogger logger = NullLogger.Instance;

        private readonly Action<object?> RunClientAsync;

        private Socket? listener;

        protected SslSocketServer()
        {
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

        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cleanup")]
        public void Stop()
        {            
            var socket = this.listener;
            this.listener = null;
            if (socket != null)
            {
                try { socket.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// Gets the authentication options to use for a newly connected client.
        /// </summary>
        /// <param name="connectedClientStream"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="InvalidDataException">Thrown if a connection frame cannot be retrieved from the socket</exception>
        /// <exception cref="AuthenticationException">Thrown if no supported authentication scheme was provided by the client.</exception>
        protected abstract Task<AuthenticationServerOptions> GetAuthenticationOptionsAsync(Stream connectedClientStream, CancellationToken cancellationToken);


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

        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Logging errors")]
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "False positive")]
        private async Task ListenForConnectionsAsync(PipeOptions sendOptions, PipeOptions receiveOptions)
        {
            try
            {
                var localEndPoint = this.listener?.LocalEndPoint;
                this.logger.LogInformation("Begin ListenForConnectionsAsync on local end point: {LocalEndPoint}.", localEndPoint);

                while (true)
                {
                    var listener = this.listener;
                    if (listener == null)
                    {
                        this.logger.LogInformation("End ListenForConnectionsAsync after close. Local end point: {LocalEndPoint}.", localEndPoint);
                        break;
                    }

                    Socket clientSocket;
                    try
                    {
                        clientSocket = await listener.AcceptAsync().ContextFree();
                    }
                    catch( Exception x ) when ((x is ObjectDisposedException || x is SocketException) && this.listener == null )
                    {
                        // Continue to allow end logging.
                        continue;
                    }

                    var remoteEndPoint = clientSocket.RemoteEndPoint;
                    if (remoteEndPoint != null)
                    {
                        this.logger.LogInformation("Client connection accepted. Local end point: {LocalEndPoint}, remote end point: {RemoteEndPoint}'.", clientSocket.LocalEndPoint, remoteEndPoint);

                        SocketConnection.SetRecommendedServerOptions(clientSocket);

                        Stream? socketStream = new NetworkStream(clientSocket, true);

                        try
                        {
                            AuthenticationServerOptions? authenticationOptions = null; 
                            try
                            {
                                authenticationOptions = await this.GetAuthenticationOptionsAsync(socketStream, default).ContextFree();
                            }
                            catch( Exception x )
                            {
                                this.logger.LogInformation(x, "GetAuthenticationOptionsAsync failed. Local end point: {LocalEndPoint}, remote end point: {RemoteEndPoint}'.", clientSocket.LocalEndPoint, remoteEndPoint);
                                socketStream.Dispose();
                                socketStream = null;
                            }


                            IPrincipal? user = null;
                            if (authenticationOptions != null)
                            {
                                if (authenticationOptions is SslServerOptions sslOptions)
                                {
                                    if (sslOptions.ServerCertificate != null)
                                    {
                                        var sslStream = new SslStream(socketStream);
                                        try
                                        {
                                            await sslStream.AuthenticateAsServerAsync(sslOptions.ServerCertificate,
                                                sslOptions.ClientCertificateRequired,
                                                sslOptions.EnabledSslProtocols,
                                                sslOptions.CertificateRevocationCheckMode != X509RevocationMode.NoCheck).ContextFree();

                                            socketStream = sslStream;
                                        }
                                        catch (Exception x)
                                        {
                                            this.logger.LogInformation(x, "SslStream.AuthenticateAsServerAsync failed. Local end point: {LocalEndPoint}, remote end point: {RemoteEndPoint}'.", clientSocket.LocalEndPoint, remoteEndPoint);

                                            try { sslStream.Dispose(); } catch { }
                                            socketStream = null;
                                        }

                                    }
                                }
                                else if (authenticationOptions is NegotiateServerOptions negotiateOptions)
                                {
                                    var negotiateStream = new NegotiateStream(socketStream);
                                    try
                                    {
                                        await negotiateStream.AuthenticateAsServerAsync(
                                            negotiateOptions.Credential ?? CredentialCache.DefaultNetworkCredentials,
                                            ProtectionLevel.EncryptAndSign, TokenImpersonationLevel.Identification).ContextFree();
                                        user = CreatePrincipal(negotiateStream.RemoteIdentity);
                                        socketStream = negotiateStream;
                                    }
                                    catch (Exception x)
                                    {
                                        this.logger.LogInformation(x, "NegotiateStream.AuthenticateAsServerAsync failed. Local end point: {LocalEndPoint}, remote end point: {RemoteEndPoint}'.", clientSocket.LocalEndPoint, remoteEndPoint);
                                        try { negotiateStream.Dispose(); } catch { }
                                        socketStream = null;
                                    }
                                }
                            }

                            if (socketStream != null)
                            {
                                IDuplexPipe? pipe = StreamConnection.GetDuplex(socketStream, sendOptions, receiveOptions);
                                
                                try
                                {
                                    if (!(pipe is IDisposable))
                                    {
                                        // Rather dummy, we need to dispose the stream when pipe is disposed, but
                                        // this is not performed by the pipe returned by StreamConnection.
                                        pipe = new OwnerDuplexPipe(pipe, socketStream);
                                    }
                                    socketStream = null;

                                    StartOnScheduler((receiveOptions ?? PipeOptions.Default).ReaderScheduler, this.RunClientAsync,
                                        new ClientConnection(pipe, remoteEndPoint, user)); // boxed, but only once per client
                                    pipe = null;
                                }
                                finally
                                {
                                    (pipe as IDisposable)?.Dispose();
                                }
                            }
                        }
                        finally
                        {
                            socketStream?.Dispose();
                        }
                    }
                }
            }
            catch (Exception ex) 
            {
                this.logger.LogInformation(ex, "Unexpected exception in ListenForConnectionsAsync.");

                this.OnServerFaulted(ex); 
            }
        }

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
