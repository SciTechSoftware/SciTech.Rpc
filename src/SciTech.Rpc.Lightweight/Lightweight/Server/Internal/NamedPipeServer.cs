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
using SciTech.Threading;
using System;
using System.Diagnostics;
using System.IO.Pipelines;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server.Internal
{
    /// <summary>
    /// A named pipe server, inspired by the <see cref="SocketServer"/> implementation.
    /// </summary>
    internal abstract class NamedPipeServer : IAsyncDisposable
    {
#if !COREFX
        private static readonly PipeSecurity DefaultSecurity = CreateDefaultSecurity();
#endif

        private readonly Action<object?> RunClientAsync;

#pragma warning disable CA2213  // Disposable fields should be disposed
        // TODO: Try to dispose safely.
        private CancellationTokenSource? listenerCts;
#pragma warning restore CA2213  // Disposable fields should be disposed

        private PipeNameHolder? pipeNameHolder;

        private Uri serverUri;

        protected NamedPipeServer(Uri serverUri)
        {
            this.serverUri = serverUri;

            this.RunClientAsync = async boxed =>
            {
                if (boxed is ClientConnection client)
                {
#pragma warning disable CA1031 // Do not catch general exception types
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
#pragma warning restore CA1031 // Do not catch general exception types
                }
            };
        }

        public string PipeName => this.pipeNameHolder?.PipeName ?? "";


        /// <summary>
        /// Start listening as a server
        /// </summary>
        public void Listen(int listenBacklog = -1, System.IO.Pipelines.PipeOptions? sendOptions = null, System.IO.Pipelines.PipeOptions? receiveOptions = null)
        {
            if (this.pipeNameHolder != null) throw new InvalidOperationException("Server is already running");

            this.pipeNameHolder = PipeUri.CreatePipeName(this.serverUri);
            this.listenerCts = new CancellationTokenSource();
            var cancellationToken = this.listenerCts.Token;
            StartOnScheduler(receiveOptions?.ReaderScheduler, _ => this.ListenForConnectionsAsync(
                listenBacklog,
                sendOptions ?? System.IO.Pipelines.PipeOptions.Default,
                receiveOptions ?? System.IO.Pipelines.PipeOptions.Default,
                cancellationToken).Forget(), null);

            this.OnStarted();
        }

        public void Stop()
        {
            this.pipeNameHolder?.Dispose();
            this.pipeNameHolder = null;

            this.listenerCts?.Cancel();
            this.listenerCts = null;
        }

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
        protected virtual void OnStarted() { }

        private static NamedPipeServerStream CreatePipeServerStream(string pipeName, int listenBacklog)
        {
            NamedPipeServerStream? pipeServerStream;
#if COREFX
            pipeServerStream = new NamedPipeServerStream(pipeName, PipeDirection.InOut, listenBacklog, PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
            var acl = pipeServerStream.GetAccessControl();

            acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
            acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
#else

            pipeServerStream = new NamedPipeServerStream(pipeName, PipeDirection.InOut, listenBacklog,
                PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous,
                0, 0,
                DefaultSecurity);
#endif
            return pipeServerStream;
        }

#if !COREFX
        private static PipeSecurity CreateDefaultSecurity()
        {
            var sec = new PipeSecurity();
            // Deny network access
            sec.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
            // Allow everyone on the machine to connect
            sec.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
            // Allow the current user should have full control.
            sec.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
            return sec;
        }
#endif

        private static void StartOnScheduler(PipeScheduler? scheduler, Action<object?> callback, object? state)
        {
            if (scheduler == PipeScheduler.Inline) scheduler = null;
            (scheduler ?? PipeScheduler.ThreadPool).Schedule(callback, state);
        }

        ValueTask IAsyncDisposable.DisposeAsync()
        {
            this.Stop();
            return default;
        }

        private async Task ListenForConnectionsAsync(int listenBacklog, System.IO.Pipelines.PipeOptions sendOptions, System.IO.Pipelines.PipeOptions receiveOptions, CancellationToken cancellationToken)
        {
#pragma warning disable CA1031 // Do not catch general exception types
#pragma warning disable CA2000 // Dispose objects before losing scope

            try
            {
                while (true)
                {
                    string pipeName = this.PipeName;
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    Debug.Assert(!string.IsNullOrEmpty(pipeName));

                    NamedPipeServerStream? pipeServerStream = CreatePipeServerStream(pipeName, listenBacklog); 
                    try
                    {
                        await pipeServerStream.WaitForConnectionAsync(cancellationToken).ContextFree();

                        var pipe = StreamConnection.GetDuplex(pipeServerStream, sendOptions, receiveOptions);
                        if (!(pipe is IDisposable))
                        {
                            // Rather dummy, we need to dispose the stream when pipe is disposed, but
                            // this is not performed by the pipe returned by StreamConnection.
                            pipe = new OwnerDuplexPipe(pipe, pipeServerStream);
                        }
                        pipeServerStream = null;

                        StartOnScheduler(receiveOptions.ReaderScheduler, this.RunClientAsync, new ClientConnection(pipe)); // boxed, but only once per client
                    }
                    finally
                    {
                        try { pipeServerStream?.Dispose(); } catch { }
                    }
                }
            }
            catch (Exception ex) { this.OnServerFaulted(ex); }

#pragma warning restore CA1031 // Do not catch general exception types
#pragma warning restore CA2000 // Dispose objects before losing scope
        }

        /// <summary>
        /// The state of a client connection
        /// </summary>
        protected readonly struct ClientConnection
        {
            internal ClientConnection(IDuplexPipe transport)
            {
                this.Transport = transport;
            }

            /// <summary>
            /// The transport to use for this connection
            /// </summary>
            public IDuplexPipe Transport { get; }
        }
    }
}
