﻿#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB and TA Instrument Inc.
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Client
{
    public class RpcConnectionManager : IRpcConnectionManager
    {

        private readonly Dictionary<RpcServerId, IRpcChannel> idToKnownConnection
            = new Dictionary<RpcServerId, IRpcChannel>();

        private readonly Dictionary<RpcServerId, WeakReference<IRpcChannel>> idToServerConnection
            = new Dictionary<RpcServerId, WeakReference<IRpcChannel>>();

        private readonly object syncRoot = new object();

        private readonly Dictionary<Uri, IRpcChannel> urlToKnownConnection
            = new Dictionary<Uri, IRpcChannel>();

        private readonly Dictionary<Uri, WeakReference<IRpcChannel>> urlToServerConnection
            = new Dictionary<Uri, WeakReference<IRpcChannel>>();

        private ImmutableArray<IRpcConnectionProvider> connectionProviders;

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
                    || (hostUrl != null && this.urlToKnownConnection.ContainsKey(hostUrl))
                    || this.idToServerConnection.ContainsKey(channel.ConnectionInfo.ServerId)
                    || (hostUrl != null && this.urlToServerConnection.ContainsKey(hostUrl)))
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
                    return existingConnection;
                }

                var wrNewConnection = new WeakReference<IRpcChannel>(newConnection);
                if (connectionInfo.ServerId != RpcServerId.Empty)
                {
                    this.idToServerConnection[connectionInfo.ServerId] = wrNewConnection;
                }

                if (connectionInfo.HostUrl != null)
                {
                    if (!this.urlToKnownConnection.TryGetValue(connectionInfo.HostUrl, out var currUrlConnection)
                        || currUrlConnection.ConnectionInfo.ServerId == RpcServerId.Empty)
                    {
                        this.urlToServerConnection[connectionInfo.HostUrl] = wrNewConnection;
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
                wrConnections.AddRange(this.idToServerConnection.Values);
                wrConnections.AddRange(this.urlToServerConnection.Values);

                this.idToServerConnection.Clear();
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

        protected virtual IRpcChannel CreateServerConnection(RpcConnectionInfo serverConnectionInfo)
        {
            foreach (var connectionProvider in this.connectionProviders)
            {
                if (connectionProvider.CanCreateChannel(serverConnectionInfo))
                {
                    return connectionProvider.CreateChannel(serverConnectionInfo, this.Options);
                }
            }

            throw new NotSupportedException("Cannot create a connection for the specified connection info.");
        }

        private IRpcChannel? GetExistingConnection(RpcConnectionInfo connectionInfo)
        {
            if (connectionInfo.ServerId != RpcServerId.Empty)
            {
                if (this.idToKnownConnection.TryGetValue(connectionInfo.ServerId, out var knownIdConnection))
                {
                    return knownIdConnection;
                }
            }
            else if (connectionInfo.HostUrl != null)
            {
                if (this.urlToKnownConnection.TryGetValue(connectionInfo.HostUrl, out var knownUrlConnection))
                {
                    return knownUrlConnection;
                }
            }

            return null;

            //if (connectionInfo.ServerId != RpcServerId.Empty
            //    && this.idToServerConnection.TryGetValue(connectionInfo.ServerId, out var wrIdConnection)
            //    && wrIdConnection.TryGetTarget(out var idConnection))
            //{
            //    return idConnection;
            //}

            //if (!string.IsNullOrWhiteSpace(connectionInfo.HostUrl)
            //    && this.urlToServerConnection.TryGetValue(connectionInfo.HostUrl, out var wrUrlConnection)
            //    && wrUrlConnection.TryGetTarget(out var urlConnection))
            //{
            //    // Found by URL but not id. Let's update the id lookup if we have an id now.
            //    if (connectionInfo.ServerId != RpcServerId.Empty)
            //    {
            //        if( urlConnection.ConnectionInfo.ServerId == RpcServerId.Empty)
            //        {
            //            // The current connection does not have a server id, but the new 
            //            // one does. Let's prefer the connection with server id
            //            this.urlToServerConnection[connectionInfo.HostUrl] = wrUrlConnection;
            //            urlConnection.ConnectionInfo.SetServerId(connectionInfo.ServerId);
            //        }

            //        this.idToServerConnection[connectionInfo.ServerId] = wrUrlConnection;
            //    }

            //    return urlConnection;
            //}

            //return null;
        }

    }
}
