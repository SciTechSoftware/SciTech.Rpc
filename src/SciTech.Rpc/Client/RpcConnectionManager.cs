#region Copyright notice and license

// Copyright (c) SciTech Software AB and contributors.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License.
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//

#endregion

using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Client
{
    public class RpcConnectionManager : IRpcConnectionManager
    {
        private readonly Dictionary<RpcServerId, WeakReference<IRpcChannel>> idToConnection
            = new Dictionary<RpcServerId, WeakReference<IRpcChannel>>();

        private readonly Dictionary<RpcServerId, IRpcChannel> idToKnownConnection
                    = new Dictionary<RpcServerId, IRpcChannel>();

        private readonly object syncRoot = new object();

        private readonly Dictionary<Uri, WeakReference<IRpcChannel>> urlToConnection
            = new Dictionary<Uri, WeakReference<IRpcChannel>>();

        private readonly Dictionary<Uri, IRpcChannel> urlToKnownConnection
                    = new Dictionary<Uri, IRpcChannel>();

        private readonly ImmutableArray<IRpcConnectionProvider> connectionProviders;

        // Constructor overload currently removed, since it causes ambiguity when using
        // dependency injection.
        public RpcConnectionManager(params IRpcConnectionProvider[] connectionProviders)
            : this((IEnumerable<IRpcConnectionProvider>)connectionProviders)
        {
        }

        public RpcConnectionManager(IEnumerable<IRpcConnectionProvider> connectionProviders, IRpcClientOptions? options = null)
        {
            this.connectionProviders = connectionProviders.ToImmutableArray();
            this.Options = new ImmutableRpcClientOptions(options);
        }

        public ImmutableRpcClientOptions Options { get; }

        public void AddKnownChannel(IRpcChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));

            var connectionInfo = channel.ConnectionInfo;
            if (connectionInfo == null) throw new InvalidOperationException("Channel has no connection info");

            Uri? hostUrl = connectionInfo.HostUrl;
            if (connectionInfo.ServerId == RpcServerId.Empty && hostUrl == null)
            {
                throw new ArgumentException("Known connection must include a ServerId or HostUrl.");
            }

            lock (this.syncRoot)
            {
                if (this.idToKnownConnection.ContainsKey(channel.ConnectionInfo.ServerId)
                    || (hostUrl != null && this.urlToConnection.ContainsKey(hostUrl)))
                {
                    throw new InvalidOperationException($"Known connection '{channel}' already added.");
                }

                if (connectionInfo.ServerId != RpcServerId.Empty)
                {
                    this.idToKnownConnection.Add(connectionInfo.ServerId, channel);
                }

                if (connectionInfo.HostUrl != null)
                {
                    this.urlToKnownConnection.Add(connectionInfo.HostUrl, channel);
                }
            }
        }

        public virtual IRpcChannel CreateServerConnection(RpcConnectionInfo connectionInfo)
        {
            foreach (var connectionProvider in this.connectionProviders)
            {
                if (connectionProvider.CanCreateChannel(connectionInfo))
                {
                    return connectionProvider.CreateChannel(connectionInfo, this.Options);
                }
            }

            throw new NotSupportedException("Cannot create a connection for the specified connection info.");
        }

        public IRpcChannel GetServerConnection(RpcConnectionInfo connectionInfo)
        {
            if (connectionInfo is null) throw new ArgumentNullException(nameof(connectionInfo));

            lock (this.syncRoot)
            {
                IRpcChannel? existingConnection = this.GetExistingConnection(connectionInfo);

                if (existingConnection != null)
                {
                    return existingConnection;
                }
            }

            var newConnection = this.CreateServerConnection(connectionInfo);
            lock (this.syncRoot)
            {
                IRpcChannel? existingConnection = this.GetExistingConnection(connectionInfo);

                if (existingConnection != null)
                {
                    // Somebody beat us to it, let's just shut down the newConnection and return the already created one.
                    newConnection.Dispose();
                    return existingConnection;
                }

                var wrNewConnection = new WeakReference<IRpcChannel>(newConnection);
                if (connectionInfo.ServerId != RpcServerId.Empty)
                {
                    this.idToConnection[connectionInfo.ServerId] = wrNewConnection;

                    if (connectionInfo.HostUrl != null)
                    {
                        this.urlToConnection[connectionInfo.HostUrl] = wrNewConnection;
                    }
                }

                return newConnection;
            }
        }

        public TService GetServiceInstance<TService>(RpcObjectRef serviceRef, SynchronizationContext? syncContext) where TService : class
        {
            if (serviceRef == null)
            {
                throw new ArgumentNullException(nameof(serviceRef));
            }

            var connection = serviceRef.ServerConnection;
            if (connection == null)
            {
                throw new ArgumentException("ServiceRef connection not initialized.", nameof(serviceRef));
            }

            var serverConnection = this.GetServerConnection(connection);
            return serverConnection.GetServiceInstance<TService>(serviceRef.ObjectId, serviceRef.ImplementedServices, syncContext);
        }

        public TService GetServiceSingleton<TService>(RpcConnectionInfo connectionInfo, SynchronizationContext? syncContext) where TService : class
        {
            var serverConnection = this.GetServerConnection(connectionInfo);
            return serverConnection.GetServiceSingleton<TService>(syncContext);
        }

        public void InvalidateConnections(RpcConnectionInfo rpcConnectionInfo)
        {
            if (rpcConnectionInfo == null) return;

            lock (this.syncRoot)
            {
                if (rpcConnectionInfo.ServerId != RpcServerId.Empty)
                {
                    this.idToConnection.Remove(rpcConnectionInfo.ServerId);
                }

                if (rpcConnectionInfo.HostUrl != null)
                {
                    this.urlToConnection.Remove(rpcConnectionInfo.HostUrl);
                }
            }
        }

        public bool RemoveKnownChannel(IRpcChannel channel)
        {
            var connectionInfo = channel?.ConnectionInfo;
            if (connectionInfo == null)
            {
                return false;
            }

            bool removed = false;
            lock (this.syncRoot)
            {
                if (connectionInfo.ServerId != RpcServerId.Empty)
                {
                    if (this.idToKnownConnection.TryGetValue(connectionInfo.ServerId, out var currConnection))
                    {
                        if (channel == currConnection)
                        {
                            this.idToKnownConnection.Remove(connectionInfo.ServerId);
                            removed = true;
                        }
                    }
                }

                if (connectionInfo.HostUrl != null)
                {
                    if (this.urlToKnownConnection.TryGetValue(connectionInfo.HostUrl, out var currConnection))
                    {
                        if (channel == currConnection)
                        {
                            this.urlToKnownConnection.Remove(connectionInfo.HostUrl);
                            removed = true;
                        }
                    }
                }
            }

            return removed;
        }

        public async Task ShutdownAsync()
        {
            var wrConnections = new List<WeakReference<IRpcChannel>>();
            lock (this.syncRoot)
            {
                wrConnections.AddRange(this.idToConnection.Values);
                wrConnections.AddRange(this.urlToConnection.Values);

                this.idToConnection.Clear();
                this.urlToKnownConnection.Clear();
            }

            List<Task> shutdownTasks = new List<Task>();
            foreach (var wrConnection in wrConnections)
            {
                if (wrConnection.TryGetTarget(out var connection))
                {
                    shutdownTasks.Add(connection.ShutdownAsync());
                }
            }

            await Task.WhenAll(shutdownTasks).ContextFree();
        }

        private static bool CanUseChannel(IRpcChannel channel)
        {
            if( channel is IRpcConnection connection )
            {
                return connection.ConnectionState is RpcConnectionState.Connecting or RpcConnectionState.Connected;
            }

            // No connection state available, so we assume the channel is usable.
            return true;
        }

        private IRpcChannel? GetExistingConnection(RpcConnectionInfo connectionInfo)
        {
            IRpcChannel? candidateConnection = null;

            if (connectionInfo.ServerId != RpcServerId.Empty)
            {
                if (this.idToKnownConnection.TryGetValue(connectionInfo.ServerId, out var knownIdChannel))
                {
                    candidateConnection = knownIdChannel;
                }

                if (this.idToConnection.TryGetValue(connectionInfo.ServerId, out var wrIdChannel)
                    && wrIdChannel.TryGetTarget(out var idChannel)
                    && CanUseChannel(idChannel)
                    && idChannel.ConnectionInfo.HasHigherPriority(candidateConnection?.ConnectionInfo))
                {
                    candidateConnection = idChannel;
                }
            }

            if (connectionInfo.HostUrl != null)
            {
                if (this.urlToKnownConnection.TryGetValue(connectionInfo.HostUrl, out var knownUrlChannel)
                    && (connectionInfo.ServerId == RpcServerId.Empty || knownUrlChannel.ConnectionInfo.ServerId == connectionInfo.ServerId )
                    && knownUrlChannel.ConnectionInfo.HasHigherPriority(candidateConnection?.ConnectionInfo))
                {
                    candidateConnection = knownUrlChannel;
                }

                if (this.urlToConnection.TryGetValue(connectionInfo.HostUrl, out var wrUrlChannel)
                    && wrUrlChannel.TryGetTarget(out var urlChannel)
                    && CanUseChannel(urlChannel)
                    && (connectionInfo.ServerId == RpcServerId.Empty || urlChannel.ConnectionInfo.ServerId == connectionInfo.ServerId)
                    && urlChannel.ConnectionInfo.HasHigherPriority(candidateConnection?.ConnectionInfo))
                {
                    candidateConnection = urlChannel;
                }
            }

            if (candidateConnection != null
                && !connectionInfo.HasHigherPriority(candidateConnection.ConnectionInfo))
            {
                return candidateConnection;
            }

            return null;
        }
    }
}