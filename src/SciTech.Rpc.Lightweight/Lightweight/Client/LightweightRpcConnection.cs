#region Copyright notice and license

// Copyright (c) SciTech Software AB and contributors.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License.
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//

#endregion Copyright notice and license

using SciTech.Rpc.Client;
using SciTech.Rpc.Lightweight.Client.Internal;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Lightweight.Server;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server;
using SciTech.Threading;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Client
{
    /// <summary>
    /// <para>
    /// Represents a connection to a <see cref="LightweightRpcServer"/>.
    /// </para>
    /// <para>To create a connection, use one of the derived classes,
    /// e.g. <see cref="TcpRpcConnection"/>, <see cref="NamedPipeRpcConnection"/>, or <see cref="InprocRpcConnection"/>.
    /// Alternatively, a <see cref="LightweightConnectionProvider"/> can be registered with an <see cref="IRpcConnectionManager"/>
    /// and then connections can be retrieved using <see cref="IRpcConnectionManager.GetServerConnection(RpcConnectionInfo)"/>.
    /// </para>
    /// </summary>
    public abstract class LightweightRpcConnection : RpcConnection
    {
        public const int DefaultMaxRequestMessageSize = 4 * 1024 * 1024;

        public const int DefaultMaxResponseMessageSize = 4 * 1024 * 1024;

        private RpcPipelineClient? connectedClient;

        private TaskCompletionSource<RpcPipelineClient>? connectionTcs;

        private WeakListener listener;

        protected LightweightRpcConnection(
            RpcConnectionInfo connectionInfo,
            IRpcClientOptions? options,
            LightweightOptions? lightweightOptions)
            : this(connectionInfo, options, LightweightProxyGenerator.Default, lightweightOptions)
        {
        }

        private protected LightweightRpcConnection(
            RpcConnectionInfo connectionInfo,
            IRpcClientOptions? options,
            LightweightProxyGenerator proxyGenerator,
            LightweightOptions? lightweightOptions)
            : base(connectionInfo, options, proxyGenerator)
        {
            this.KeepSizeLimitedConnectionAlive = lightweightOptions?.KeepSizeLimitedConnectionAlive ?? true;
            this.AllowReconnect = lightweightOptions?.AllowReconnect ?? false;
            this.UseOperationNumbers = lightweightOptions?.UseOperationNumbers ?? true;
            this.listener = new WeakListener(this);
        }

        ~LightweightRpcConnection()
        {
            TaskCompletionSource<RpcPipelineClient>? connectionTcs;
            RpcPipelineClient? connectedClient;
            lock (this.SyncRoot)
            {
                if (this.ConnectionState == RpcConnectionState.Disconnected)
                {
                    // Already disconnected, why wasn't finalizer suppressed?
                    Debug.Assert(this.connectedClient == null);
                    return;
                }

                connectedClient = this.connectedClient;
                this.connectedClient = null;

                connectionTcs = this.connectionTcs;
                this.connectionTcs = null;

                this.OnConnectionResetSynchronized();
            }

            connectionTcs?.TrySetCanceled();

            if (connectedClient != null)
            {
                connectedClient.CloseAsync().Forget();
            }
        }

        /// <summary>
        /// Gets a value indicating if the connection should try to reconnect after it has been lost.
        /// </summary>
        public bool AllowReconnect { get; }

        /// <inheritdoc/>
        public override bool IsConnected => this.connectedClient != null;

        /// <summary>
        /// Gets a value indicating if the connection should be kept alive even if the size of a message
        /// exceeds the limits specified by <see cref="RpcClientOptions"/> and <see cref="RpcServerOptions"/>.
        /// </summary>
        public bool KeepSizeLimitedConnectionAlive { get; }

        /// <summary>
        /// Gets a value indicating whether operation numbers are used instead of operation names, if the server supports
        /// them. See <see cref="LightweightOptions.UseOperationNumbers"/>.
        /// </summary>
        public bool UseOperationNumbers { get; }

        /// <inheritdoc/>
        public override Task ConnectAsync(CancellationToken cancellationToken)
            => this.ConnectClientAsync(cancellationToken).AsTask();

        /// <inheritdoc/>
        public override async Task ShutdownAsync()
        {
            var prevClient = await this.ResetConnectionAsync(RpcConnectionState.Disconnected, null).ContextFree();

            if (prevClient != null)
            {
                await prevClient.WaitFinishedAsync().ContextFree();
            }

            this.NotifyDisconnected();
        }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Handled by ShutdownAsync")]
        internal ValueTask<RpcPipelineClient> ConnectClientAsync(CancellationToken cancellationToken)
        {
            Task<RpcPipelineClient>? activeConnectionTask = null;
            lock (this.SyncRoot)
            {
                if (this.connectedClient != null)
                {
                    return new ValueTask<RpcPipelineClient>(this.connectedClient);
                }

                if (this.ConnectionState == RpcConnectionState.Disconnected)
                {
                    throw new ObjectDisposedException(this.ToString());
                }
                if (this.ConnectionState == RpcConnectionState.ConnectionFailed || this.ConnectionState == RpcConnectionState.ConnectionLost)
                {
                    if (!this.AllowReconnect)
                    {
                        throw new RpcCommunicationException(this.ConnectionState == RpcConnectionState.ConnectionLost
                            ? RpcCommunicationStatus.ConnectionLost : RpcCommunicationStatus.Unavailable);
                    }
                }

                if (this.connectionTcs != null)
                {
                    activeConnectionTask = this.connectionTcs.Task;
                }
                else
                {
                    this.SetConnectionState(RpcConnectionState.Connecting);
                    this.connectionTcs = new TaskCompletionSource<RpcPipelineClient>();
                }
            }

            if (activeConnectionTask != null)
            {
                async ValueTask<RpcPipelineClient> AwaitConnection()
                {
                    return await activeConnectionTask!.ContextFree();
                }

                return AwaitConnection();
            }

            async ValueTask<RpcPipelineClient> DoConnect()
            {
                try
                {
                    int sendMaxMessageSize = this.Options?.SendMaxMessageSize ?? DefaultMaxRequestMessageSize;
                    int receiveMaxMessageSize = this.Options?.ReceiveMaxMessageSize ?? DefaultMaxResponseMessageSize;

                    var connection = await this.ConnectPipelineAsync(sendMaxMessageSize, receiveMaxMessageSize, cancellationToken).ContextFree();

                    var connectedClient = new RpcPipelineClient(connection, sendMaxMessageSize, receiveMaxMessageSize, this.KeepSizeLimitedConnectionAlive, this.UseOperationNumbers);

                    connectedClient.Faulted += this.listener.PipelineFaulted;

                    connectedClient.RunAsyncCore().Forget();

                    TaskCompletionSource<RpcPipelineClient>? connectionTcs;
                    lock (this.SyncRoot)
                    {
                        if( this.ConnectionState != RpcConnectionState.Connecting)
                        {
                            throw new RpcCommunicationException(RpcCommunicationStatus.ConnectionLost);
                        }

                        this.connectedClient = connectedClient;
                        connectionTcs = this.connectionTcs;
                        this.connectionTcs = null;
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
                    lock (this.SyncRoot)
                    {
                        if (this.connectionTcs != null)
                        {
                            Debug.Assert(this.connectedClient == null);

                            connectionTcs = this.connectionTcs;
                            this.connectionTcs = null;
                        }

                        this.SetConnectionState(RpcConnectionState.ConnectionFailed);
                    }

                    connectionTcs?.SetException(e);

                    this.NotifyConnectionFailed();

                    throw;
                }
            }

            return DoConnect();
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        protected abstract Task<IDuplexPipe> ConnectPipelineAsync(int sendMaxMessageSize, int receiveMaxMessageSize, CancellationToken cancellationToken);

        [EditorBrowsable(EditorBrowsableState.Never)]
        protected override IRpcSerializer CreateDefaultSerializer()
        {
            return new JsonRpcSerializer();
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        protected virtual void OnConnectionResetSynchronized()
        {
        }

        private static Exception? TranslateConnectionException(Exception? ex, RpcConnectionState newState)
        {
            if (ex != null)
            {
                return newState switch
                {
                    RpcConnectionState.ConnectionLost => new RpcCommunicationException(RpcCommunicationStatus.ConnectionLost, "Lightweight connection lost.", ex),
                    RpcConnectionState.ConnectionFailed => new RpcCommunicationException(RpcCommunicationStatus.Unavailable, "Failed to connect.", ex),
                    _ => new RpcCommunicationException(RpcCommunicationStatus.Unknown, "Lightweight connection lost.", ex)
                };
            }

            return null;
        }

        /// <param name="faultedClient">The client whose pipeline has faulted, or <c>null</c> to reset the connection
        /// regardless of the current client.</param>
        private async Task<RpcPipelineClient?> ResetConnectionAsync(RpcConnectionState state, Exception? ex, RpcPipelineClient? faultedClient = null)
        {
            TaskCompletionSource<RpcPipelineClient>? connectionTcs;
            RpcPipelineClient? connectedClient;
            lock (this.SyncRoot)
            {
                if (this.ConnectionState == RpcConnectionState.Disconnected)
                {
                    // Already disconnected
                    Debug.Assert(this.connectedClient == null);
                    return null;
                }

                if (faultedClient != null && this.connectedClient != null && this.connectedClient != faultedClient)
                {
                    // A fault from a previous pipeline (e.g. reported by both a failed write and the receive loop)
                    // must not reset a reconnected client.
                    return null;
                }

                connectedClient = this.connectedClient;
                this.connectedClient = null;

                connectionTcs = this.connectionTcs;
                this.connectionTcs = null;

                this.OnConnectionResetSynchronized();

                this.SetConnectionState(state);

                if (state == RpcConnectionState.Disconnected)
                {
                    // No need to finalize
                    GC.SuppressFinalize(this);
                }
            }

            connectionTcs?.TrySetCanceled();

            // TODO: wait for unfinished frames?
            if (connectedClient != null)
            {
                connectedClient.Faulted -= this.listener.PipelineFaulted;
                await connectedClient.CloseAsync(TranslateConnectionException(ex, state)).ContextFree();
            }

            return connectedClient;
        }

        private sealed class WeakListener
        {
            private readonly WeakReference<LightweightRpcConnection> wrConnection;

            public WeakListener(LightweightRpcConnection self)
            {
                this.wrConnection = new WeakReference<LightweightRpcConnection>(self);
            }

            internal void PipelineFaulted(object? sender, ExceptionEventArgs e)
            {
                if (this.wrConnection.TryGetTarget(out var self))
                {
                    // ResetConnectionAsync will call CloseAsync which will wait for the receive loop to exit.
                    // Since PipelineFaulted may be invoked from the receive loop, it's important that this
                    // method does not block. The connected client is cleared synchronously, before the first await,
                    // so that a failed write is not followed by a new call on the same pipeline.

                    ResetAndNotifyAsync().Forget();

                    async Task ResetAndNotifyAsync()
                    {
                        var prevClient = await self.ResetConnectionAsync(RpcConnectionState.ConnectionLost, e.Exception, sender as RpcPipelineClient).ContextFree();
                        if (prevClient != null)
                        {
                            self.NotifyConnectionLost();
                        }
                    }
                }
            }
        }
    }
}