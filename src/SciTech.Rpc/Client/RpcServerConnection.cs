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
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Client
{
    /// <summary>
    /// Base implementation of the <see cref="IRpcServerConnection"/> interface.
    /// </summary>
    public abstract class RpcServerConnection : IRpcServerConnection
    {
        private static readonly ILog Logger = LogProvider.For<RpcServerConnection>();

        private readonly IRpcProxyGenerator proxyGenerator;

        private readonly Dictionary<RpcObjectId, List<WeakReference<RpcProxyBase>>> serviceInstances
            = new Dictionary<RpcObjectId, List<WeakReference<RpcProxyBase>>>();

        private readonly object syncRoot = new object();

        private RpcConnectionState connectionState;

        private bool hasPendingStateChange;

        private bool isDisposed;

        private volatile IRpcSerializer? serializer;

        protected RpcServerConnection(
            RpcServerConnectionInfo connectionInfo,
            IRpcClientOptions? options,
            IRpcProxyGenerator proxyGenerator)
        {
            this.ConnectionInfo = connectionInfo;
            this.proxyGenerator = proxyGenerator;
            this.Options = options?.AsImmutable() ?? ImmutableRpcClientOptions.Empty;
        }

        public event EventHandler? Connected;

        public event EventHandler? ConnectionFailed;

        public event EventHandler? ConnectionLost;

        public event EventHandler? ConnectionStateChanged;

        public event EventHandler? Disconnected;

        /// <summary>
        /// Gets the connection info of this connection.
        /// </summary>
        public RpcServerConnectionInfo ConnectionInfo { get; }

        public RpcConnectionState ConnectionState
        {
            get
            {
                lock (this.syncRoot) return this.connectionState;
            }
        }

        public abstract bool IsConnected { get; }

        public abstract bool IsEncrypted { get; }

        public abstract bool IsMutuallyAuthenticated { get; }

        public abstract bool IsSigned { get; }

        public ImmutableRpcClientOptions Options { get; }

        protected internal IRpcSerializer Serializer
        {
            get
            {
                if (this.serializer == null)
                {
                    this.serializer = this.Options.Serializer ?? this.CreateDefaultSerializer();
                }

                return this.serializer;
            }
        }

        /// <summary>
        /// Establishes a connection with the configured RPC server. It is usually not necessary to call this method 
        /// explicitly, since a connection will be established on the first RPC operation.
        /// </summary>
        /// <returns></returns>
        public abstract Task ConnectAsync(CancellationToken cancellationToken = default);

        public void Dispose()
        {
            if (!this.isDisposed)
            {
                this.Dispose(true);

                this.isDisposed = true;
            }
        }


        public TService GetServiceInstance<TService>(RpcObjectId objectId,
            IReadOnlyCollection<string>? implementedServices, SynchronizationContext? syncContext) where TService : class
        {
            if (objectId == RpcObjectId.Empty)
            {
                throw new ArgumentException("ObjectId should not be empty.", nameof(objectId));
            }

            return GetServiceInstanceCore<TService>(objectId, implementedServices, syncContext);
        }

        public TService GetServiceSingleton<TService>(SynchronizationContext? syncContext) where TService : class
        {
            // TODO: Implement singleton factories.

            return GetServiceInstanceCore<TService>(RpcObjectId.Empty, syncContext);
        }

        /// <summary>
        /// Disconnects this connection and cleans up any used resources.
        /// </summary>
        /// <returns></returns>
        public abstract Task ShutdownAsync();

        protected abstract IRpcSerializer CreateDefaultSerializer();

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if( this.IsConnected )
                {
                    Logger.Warn("Connection disposed while still connected.");
                }

                // Shut down connection in case it's still connected, but let's 
                // not wait for it to finish (to avoid dead-locks).
                this.ShutdownAsync().Forget();
            }
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

        private TService GetServiceInstanceCore<TService>(RpcObjectId refObjectId, SynchronizationContext? syncContext) where TService : class
        {
            return GetServiceInstanceCore<TService>(refObjectId, default, syncContext);
        }

        private TService GetServiceInstanceCore<TService>(
            RpcObjectId refObjectId,
            IReadOnlyCollection<string>? implementedServices,
            SynchronizationContext? syncContext) where TService : class
        {
            lock (this.syncRoot)
            {
                if (this.serviceInstances.TryGetValue(refObjectId, out var servicesList))
                {
                    foreach (var wService in servicesList)
                    {
                        if (wService.TryGetTarget(out var proxyBase)
                            && proxyBase is TService service
                            && proxyBase.SyncContext == syncContext
                            && proxyBase.ImplementsServices(implementedServices))
                        {
                            return service;
                        }
                    }
                }
            }

            RpcObjectProxyFactory serviceProxyCreator
                = this.proxyGenerator.GenerateObjectProxyFactory<TService>(implementedServices);

            lock (this.syncRoot)
            {
                if (this.serviceInstances.TryGetValue(refObjectId, out var servicesList))
                {
                    foreach (var wService in servicesList)
                    {
                        if (wService.TryGetTarget(out var proxyBase)
                            && proxyBase is TService service
                            && proxyBase.SyncContext == syncContext
                            && proxyBase.ImplementsServices(implementedServices))
                        {
                            return service;
                        }
                    }
                }
                else
                {
                    servicesList = new List<WeakReference<RpcProxyBase>>();
                    this.serviceInstances.Add(refObjectId, servicesList);
                }

                var serviceInstance = serviceProxyCreator(refObjectId, this, syncContext);
                servicesList.Add(new WeakReference<RpcProxyBase>(serviceInstance));

                return (TService)(object)serviceInstance;
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
