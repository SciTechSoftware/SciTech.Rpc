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
    /// Alternatively, a <see cref="LightweightConnectionProvider"/> can be registered with an <see cref="RpcConnectionManager"/>
    /// and then connections can be retrieved using <see cref="RpcConnectionManager.CreateServerConnection(RpcConnectionInfo)"/>.
    /// </para>
    /// </summary>
    public abstract class LightweightRpcConnection : RpcConnection
    {
        public const int DefaultMaxRequestMessageSize = 4 * 1024 * 1024;

        public const int DefaultMaxResponseMessageSize = 4 * 1024 * 1024;

        private RpcPipelineClient? connectedClient;

        private TaskCompletionSource<RpcPipelineClient>? connectionTcs;

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
        }

        /// <inheritdoc/>
        public override bool IsConnected => this.connectedClient != null;


        /// <summary>
        /// Gets a value indicating if the connection should be kept alive even if the size of a message
        /// exceeds the limits specified by <see cref="RpcClientOptions"/> and <see cref="RpcServerOptions"/>. 
        /// </summary>
        public bool KeepSizeLimitedConnectionAlive { get; }


        /// <summary>
        /// Gets a value indicating if the connection should try to reconnect after it has been lost.
        /// </summary>
        public bool AllowReconnect { get; }


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

                    var connectedClient = new RpcPipelineClient(connection, sendMaxMessageSize, receiveMaxMessageSize, this.KeepSizeLimitedConnectionAlive);

                    connectedClient.ReceiveLoopFaulted += this.ConnectedClient_ReceiveLoopFaulted;
                    connectedClient.RunAsyncCore().Forget();

                    TaskCompletionSource<RpcPipelineClient>? connectionTcs;
                    lock (this.SyncRoot)
                    {
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

        private void ConnectedClient_ReceiveLoopFaulted(object? sender, ExceptionEventArgs e)
        {
            // ResetConnectionAsync will call CloseAsync which will wait for the receive loop to exit.
            // Since ReceiveLoopFaulted is invoked from the receive loop, it's important that this 
            // method does not block.

            ResetAndNotifyAsync().Forget();

            async Task ResetAndNotifyAsync()
            {
                await this.ResetConnectionAsync(RpcConnectionState.ConnectionLost, e.Exception).ContextFree();
                this.NotifyConnectionLost();
            }
        }

        private async Task<RpcPipelineClient?> ResetConnectionAsync(RpcConnectionState state, Exception? ex)
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

                connectedClient = this.connectedClient;
                this.connectedClient = null;


                connectionTcs = this.connectionTcs;
                this.connectionTcs = null;

                this.OnConnectionResetSynchronized();

                this.SetConnectionState(state);
            }

            connectionTcs?.TrySetCanceled();

            // TODO: wait for unfinished frames?
            if (connectedClient != null)
            {
                connectedClient.ReceiveLoopFaulted -= this.ConnectedClient_ReceiveLoopFaulted;
                await connectedClient.CloseAsync(TranslateConnectionException(ex, state)).ContextFree();
            }

            return connectedClient;
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
    }
}
