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
    /// Alternatively, a <see cref="LightweightConnectionProvider"/> can be registered with an <see cref="RpcServerConnectionManager"/>
    /// and then connections can be retrieved using <see cref="RpcServerConnectionManager.CreateServerConnection(RpcServerConnectionInfo)"/>.
    /// </para>
    /// </summary>
    public abstract class LightweightRpcConnection : RpcServerConnection
    {
        public const int DefaultMaxRequestMessageSize = 4 * 1024 * 1024;

        public const int DefaultMaxResponseMessageSize = 4 * 1024 * 1024;

        private RpcPipelineClient? connectedClient;

        private TaskCompletionSource<RpcPipelineClient>? connectionTcs;

        protected LightweightRpcConnection(
            RpcServerConnectionInfo connectionInfo,
            IRpcClientOptions? options,
            IRpcProxyDefinitionsProvider? definitionsProvider,
            LightweightOptions? lightweightOptions)
            : this(connectionInfo, options, LightweightProxyGenerator.Factory.CreateProxyGenerator(definitionsProvider), lightweightOptions)
        {
        }

        private protected LightweightRpcConnection(
            RpcServerConnectionInfo connectionInfo,
            IRpcClientOptions? options,
            LightweightProxyGenerator proxyGenerator,
            LightweightOptions? lightweightOptions)
            : base(connectionInfo, options, proxyGenerator)
        {
            this.KeepSizeLimitedConnectionAlive = lightweightOptions?.KeepSizeLimitedConnectionAlive ?? true;
        }

        /// <inheritdoc/>
        public override bool IsConnected => this.connectedClient != null;


        /// <summary>
        /// Gets a value indicating  if the connection should be kept alive even if the size of a message
        /// exceeds the limits specified by <see cref="RpcClientOptions"/> and <see cref="RpcServerOptions"/>. 
        /// </summary>
        public bool KeepSizeLimitedConnectionAlive { get; }

        protected object SyncRoot { get; } = new object();

        /// <inheritdoc/>
        public override Task ConnectAsync(CancellationToken cancellationToken)
            => this.ConnectClientAsync(cancellationToken).AsTask();

        public override async Task ShutdownAsync()
        {
            var prevClient = this.ResetConnection(RpcConnectionState.Disconnected, null);

            if (prevClient != null)
            {
                await prevClient.AwaitFinished().ContextFree();
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
                try
                {
                    int sendMaxMessageSize = this.Options?.SendMaxMessageSize ?? DefaultMaxRequestMessageSize;
                    int receiveMaxMessageSize = this.Options?.ReceiveMaxMessageSize ?? DefaultMaxResponseMessageSize;

                    var connection = await this.ConnectPipelineAsync(sendMaxMessageSize, receiveMaxMessageSize, cancellationToken).ContextFree();

                    var connectedClient = new RpcPipelineClient(connection, sendMaxMessageSize, receiveMaxMessageSize, this.KeepSizeLimitedConnectionAlive);

                    connectedClient.ReceiveLoopFaulted += this.ConnectedClient_ReceiveLoopFaulted;
                    connectedClient.RunAsyncCore();

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
            return new DataContractRpcSerializer();
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        protected virtual void OnConnectionResetSynchronized()
        {

        }


        private void ConnectedClient_ReceiveLoopFaulted(object sender, ExceptionEventArgs e)
        {
            this.ResetConnection(RpcConnectionState.ConnectionLost, e.Exception);

            this.NotifyConnectionLost();
        }

        private RpcPipelineClient? ResetConnection(RpcConnectionState state, Exception? ex)
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
                connectedClient.Close(ex);
            }

            return connectedClient;
        }
    }
}
