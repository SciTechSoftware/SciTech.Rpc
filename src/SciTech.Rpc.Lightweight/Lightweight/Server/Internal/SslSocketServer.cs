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
using SciTech.Rpc.Server;
using SciTech.Threading;
using System;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server.Internal
{
    /// <summary>
    /// Modified copy of <see cref="SocketServer"/> with added support for <see cref="SslStream"/>.
    /// </summary>
    internal abstract class SslSocketServer
    {
        private readonly Action<object> RunClientAsync;

        private readonly SslServerOptions? sslOptions;

        private Socket? listener;

#pragma warning disable CA1031 // Do not catch general exception types
        protected SslSocketServer(SslServerOptions? sslOptions)
        {
            this.sslOptions = sslOptions;

            this.RunClientAsync = async boxed =>
            {
                var client = (ClientConnection)boxed;
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
            };
        }
#pragma warning restore CA1031 // Do not catch general exception types


        /// <summary>
        /// Start listening as a server
        /// </summary>
        public void Listen(EndPoint endPoint,
            AddressFamily addressFamily = AddressFamily.InterNetwork,
            SocketType socketType = SocketType.Stream,
            ProtocolType protocolType = ProtocolType.Tcp,
            int listenBacklog = 20,
            PipeOptions? sendOptions = null, PipeOptions? receiveOptions = null)
        {
            if (this.listener != null) throw new InvalidOperationException("Server is already running");

            Socket listener = new Socket(addressFamily, socketType, protocolType);
            listener.Bind(endPoint);
            listener.Listen(listenBacklog);

            this.listener = listener;
            Task.Run(() => this.ListenForConnectionsAsync(PipeOptions.Default, PipeOptions.Default)).Forget();

            StartOnScheduler(receiveOptions?.ReaderScheduler, _ => this.ListenForConnectionsAsync(
                PipeOptions.Default, PipeOptions.Default).Forget(), null);

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

        private static void StartOnScheduler(PipeScheduler? scheduler, Action<object> callback, object? state)
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
                    var clientSocket = await this.listener.AcceptAsync().ContextFree();
                    SocketConnection.SetRecommendedServerOptions(clientSocket);

                    Stream socketStream = new NetworkStream(clientSocket, true);

                    if (this.sslOptions?.ServerCertificate != null)
                    {
                        var sslStream = new SslStream(socketStream);
                        await sslStream.AuthenticateAsServerAsync(this.sslOptions.ServerCertificate,
                            this.sslOptions.ClientCertificateRequired,
                            this.sslOptions.EnabledSslProtocols,
                            this.sslOptions.CertificateRevocationCheckMode != X509RevocationMode.NoCheck).ContextFree();

                        socketStream = sslStream;
                    }


                    var pipe = StreamConnection.GetDuplex(socketStream);

                    StartOnScheduler(receiveOptions.ReaderScheduler, this.RunClientAsync,
                        new ClientConnection(pipe, clientSocket.RemoteEndPoint)); // boxed, but only once per client
                }
            }
            catch (NullReferenceException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { this.OnServerFaulted(ex); }
        }
#pragma warning restore CA1031 // Do not catch general exception types
#pragma warning restore CA2000 // Dispose objects before losing scope


        /// <summary>
        /// The state of a client connection
        /// </summary>
        protected readonly struct ClientConnection
        {
            internal ClientConnection(IDuplexPipe transport, EndPoint remoteEndPoint)
            {
                this.Transport = transport;
                this.RemoteEndPoint = remoteEndPoint;
            }

            /// <summary>
            /// The remote endpoint that the client connected from
            /// </summary>
            public EndPoint RemoteEndPoint { get; }

            /// <summary>
            /// The transport to use for this connection
            /// </summary>
            public IDuplexPipe Transport { get; }
        }
    }
}
