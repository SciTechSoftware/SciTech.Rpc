#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Client
{
    /// <summary>
    /// Base implementation of the <see cref="IRpcConnection"/> interface.
    /// </summary>
    public abstract class RpcConnection : RpcChannel, IRpcConnection
    {
        private RpcConnectionState connectionState;

        private bool hasPendingStateChange;

        protected RpcConnection(
            RpcConnectionInfo connectionInfo,
            IRpcClientOptions? options,
            IRpcProxyGenerator proxyGenerator) : base(connectionInfo, options, proxyGenerator)
        {
        }

        public event EventHandler? Connected;

        public event EventHandler? ConnectionFailed;

        public event EventHandler? ConnectionLost;

        public event EventHandler? ConnectionStateChanged;

        public event EventHandler? Disconnected;


        public RpcConnectionState ConnectionState
        {
            get
            {
                lock (this.SyncRoot) return this.connectionState;
            }
        }

        public abstract bool IsConnected { get; }

        public abstract bool IsEncrypted { get; }

        public abstract bool IsMutuallyAuthenticated { get; }

        public abstract bool IsSigned { get; }

        /// <summary>
        /// Establishes a connection with the configured RPC server. It is usually not necessary to call this method 
        /// explicitly, since a connection will be established on the first RPC operation.
        /// </summary>
        /// <returns></returns>
        public abstract Task ConnectAsync(CancellationToken cancellationToken = default);

        protected override void Dispose(bool disposing)
        {
            if (this.IsConnected)
            {
                // TODO: Logger.Warn("Connection disposed while still connected.");
            }

            base.Dispose(disposing);
        }


        protected void NotifyConnected()
        {
            this.Connected?.Invoke(this, EventArgs.Empty);

            this.RaiseStateChangdIfNecessary();
        }

        protected void NotifyConnectionFailed()
        {
            this.ConnectionFailed?.Invoke(this, EventArgs.Empty);

            this.RaiseStateChangdIfNecessary();
        }

        protected void NotifyConnectionLost()
        {
            this.ConnectionLost?.Invoke(this, EventArgs.Empty);

            this.RaiseStateChangdIfNecessary();
        }

        protected void NotifyDisconnected()
        {
            this.Disconnected?.Invoke(this, EventArgs.Empty);

            this.RaiseStateChangdIfNecessary();
        }

        /// <summary>
        /// Should be called by derived classes, within a lock when the connection state has been changed.
        /// The caller should also make a sub-sequent call to <see cref="NotifyConnectionLost"/>,
        /// <see cref="NotifyConnectionFailed"/>,  <see cref="NotifyDisconnected"/>, or <see cref="NotifyConnected"/>
        /// </summary>
        /// <param name="state"></param>
        protected void SetConnectionState(RpcConnectionState state)
        {
            if (this.connectionState != state)
            {
                this.connectionState = state;
                this.hasPendingStateChange = true;
            }
        }


        private void RaiseStateChangdIfNecessary()
        {
            bool stateChanged;
            stateChanged = this.hasPendingStateChange;
            this.hasPendingStateChange = false;

            if (stateChanged)
            {
                this.ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
