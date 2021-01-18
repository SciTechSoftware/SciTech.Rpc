#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using System;
using System.Collections.Generic;
using System.Threading;

namespace SciTech.Rpc.Client
{
    public interface IRpcConnectionProvider
    {
        bool CanCreateChannel(RpcConnectionInfo connectionInfo);

        IRpcChannel CreateChannel(RpcConnectionInfo connectionInfo, IRpcClientOptions? options);
    }

    public interface IRpcConnectionManager
    {
        void AddKnownChannel(IRpcChannel channel);

        TService GetServiceInstance<TService>(RpcObjectRef serviceRef, SynchronizationContext? syncContext) where TService : class;

        TService GetServiceSingleton<TService>(RpcConnectionInfo connectionInfo, SynchronizationContext? syncContext) where TService : class;

        IRpcChannel GetServerConnection(RpcConnectionInfo connectionInfo);
        
        bool RemoveKnownChannel(IRpcChannel channel);

        ImmutableRpcClientOptions Options { get; }
    }

    public static class RpcConnectionManagerExtensions
    {
        public static TService GetServiceInstance<TService>(this IRpcConnectionManager connectionManager, RpcObjectRef serviceRef, bool useSyncContext = true) where TService : class
        {
            if (connectionManager == null) throw new ArgumentNullException(nameof(connectionManager));

            return connectionManager.GetServiceInstance<TService>(serviceRef, useSyncContext ? SynchronizationContext.Current : null);
        }

        public static TService GetServiceInstance<TService>(this IRpcConnectionManager connectionManager, RpcObjectRef serviceRef, SynchronizationContext syncContext) where TService : class
        {
            if (connectionManager == null) throw new ArgumentNullException(nameof(connectionManager));

            return connectionManager.GetServiceInstance<TService>(serviceRef, syncContext);
        }

        public static TService GetServiceInstance<TService>(this IRpcConnectionManager connectionManager, RpcObjectRef<TService> serviceRef, SynchronizationContext syncContext) where TService : class
        {
            if (connectionManager == null) throw new ArgumentNullException(nameof(connectionManager));

            return connectionManager.GetServiceInstance<TService>(serviceRef, syncContext);
        }

        public static TService GetServiceInstance<TService>(this IRpcConnectionManager connectionManager, RpcObjectRef<TService> serviceRef, bool useSyncContext = true) where TService : class
        {
            if (connectionManager == null) throw new ArgumentNullException(nameof(connectionManager));

            return connectionManager.GetServiceInstance<TService>(serviceRef, useSyncContext ? SynchronizationContext.Current : null);
        }

        public static TService GetServiceSingleton<TService>(this IRpcConnectionManager connectionManager, RpcConnectionInfo connectionInfo, bool useSyncContext = true) where TService : class
        {
            if (connectionManager == null) throw new ArgumentNullException(nameof(connectionManager));

            return connectionManager.GetServiceSingleton<TService>(connectionInfo, useSyncContext ? SynchronizationContext.Current : null);
        }

        public static TService GetServiceSingleton<TService>(this IRpcConnectionManager connectionManager, RpcSingletonRef<TService> singletonRef, bool useSyncContext = true) where TService : class
        {
            if (connectionManager == null) throw new ArgumentNullException(nameof(connectionManager));

            if (singletonRef == null)
            {
                throw new ArgumentNullException(nameof(singletonRef));
            }

            var connection = singletonRef.ServerConnection;
            if (connection == null)
            {
                throw new ArgumentException("SingletonRef connection not initialized.", nameof(singletonRef));
            }

            return connectionManager.GetServiceSingleton<TService>(connection, useSyncContext ? SynchronizationContext.Current : null);
        }

        public static TService GetServiceSingleton<TService>(this IRpcConnectionManager connectionManager, RpcSingletonRef<TService> singletonRef, SynchronizationContext syncContext) where TService : class
        {
            if (connectionManager == null) throw new ArgumentNullException(nameof(connectionManager));

            if (singletonRef == null)
            {
                throw new ArgumentNullException(nameof(singletonRef));
            }

            var connection = singletonRef.ServerConnection;
            if (connection == null)
            {
                throw new ArgumentException("SingletonRef connection not initialized.", nameof(singletonRef));
            }

            return connectionManager.GetServiceSingleton<TService>(connection, syncContext);
        }
    }
}
