#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB.
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

using SciTech.Diagnostics;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Threading;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server.Internal
{
    /// <summary>
    /// A named pipe server, inspired by the "Pipelines.Unofficial.SocketServer" implementation.
    /// </summary>
    internal abstract class NamedPipeServer : IAsyncDisposable
    {
        private readonly Action<object?> RunClientAsync;

#pragma warning disable CA2213  // Disposable fields should be disposed
        // TODO: Try to dispose safely.
        private CancellationTokenSource? listenerCts;
#pragma warning restore CA2213  // Disposable fields should be disposed

        private PipeNameHolder? pipeNameHolder;

        private Uri serverUri;

        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cleanup")]
        protected NamedPipeServer(Uri serverUri)
        {
            this.serverUri = serverUri;

            this.RunClientAsync = async oClient =>
            {
                if (oClient is ClientConnection client)
                {
                    try
                    {
                        await this.OnClientConnectedAsync(in client).ContextFree();
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

        public string PipeName => this.pipeNameHolder?.PipeName ?? "";


        /// <summary>
        /// Start listening as a server
        /// </summary>
        public void Listen(int listenBacklog = -1)
        {
            if (this.pipeNameHolder != null) throw new InvalidOperationException("Server is already running");

            this.pipeNameHolder = PipeUri.CreatePipeName(this.serverUri);
            this.listenerCts = new CancellationTokenSource();
            var cancellationToken = this.listenerCts.Token;
            Task.Run(() => this.ListenForConnectionsAsync(listenBacklog, cancellationToken)).Forget();

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
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
#if NET5_0_OR_GREATER
                var pipeSecurity = CreateDefaultSecurity();
                pipeServerStream = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, listenBacklog, PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous,0,0,pipeSecurity);
#elif NETFRAMEWORK
                var pipeSecurity = CreateDefaultSecurity();
                pipeServerStream = new NamedPipeServerStream(pipeName, PipeDirection.InOut, listenBacklog,
                    PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous,
                    0, 0,
                    pipeSecurity);
#else
                pipeServerStream = new NamedPipeServerStream(pipeName, PipeDirection.InOut, listenBacklog, PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);

                // Is there a race here? The pipe is created with default access before updating security. On the other hand, nobody's listening yet.
                
                // Previously pipeServerStream.SetAccessControl(pipeSecurity) was used, but that caused communication to hang (i.e. connection was allowed, but pipe communication failed).
                // I have not investigated this further.
                pipeServerStream.GetAccessControl().AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
#endif
            }
            else
            {
                pipeServerStream = new NamedPipeServerStream(pipeName, PipeDirection.InOut, listenBacklog, PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
            }

            return pipeServerStream;
        }

#if NET5_0_OR_GREATER
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
        private static PipeSecurity CreateDefaultSecurity()
        {
            var sec = new PipeSecurity();
            // Deny network access
            sec.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
            // Allow everyone on the machine to connect
            sec.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
            // The current user should have full control.
            if (WindowsIdentity.GetCurrent().User is SecurityIdentifier userId)
            {
                sec.AddAccessRule(new PipeAccessRule(userId, PipeAccessRights.FullControl, AccessControlType.Allow));
            }
            return sec;
        }


        ValueTask IAsyncDisposable.DisposeAsync()
        {
            this.Stop();
            return default;
        }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types")]
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Transferred ownership")]
        private async Task ListenForConnectionsAsync(int listenBacklog, CancellationToken cancellationToken)
        {
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

                        var pipe = new StreamDuplexPipe(pipeServerStream);//, sendOptions, receiveOptions);
                        pipeServerStream = null;

                        PipeScheduler.ThreadPool.Schedule(this.RunClientAsync, new ClientConnection(pipe));
                    }
                    finally
                    {
                        if (pipeServerStream != null)
                        {
                            try { await pipeServerStream.DisposeAsync().ContextFree(); } catch { }
                        }
                    }
                }
            }
            catch (Exception ex) { this.OnServerFaulted(ex); }
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
