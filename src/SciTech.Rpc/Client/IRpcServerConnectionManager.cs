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

using System;
using System.Collections.Generic;
using System.Threading;

namespace SciTech.Rpc.Client
{
    public interface IRpcConnectionProvider
    {
        bool CanCreateConnection(RpcServerConnectionInfo connectionInfo);

        IRpcServerConnection CreateConnection(RpcServerConnectionInfo connectionInfo, IReadOnlyList<RpcClientCallInterceptor> callInterceptors);
    }

    public interface IRpcServerConnectionManager
    {
        void AddCallInterceptor(RpcClientCallInterceptor callInterceptor);

        void AddKnownConnection(IRpcServerConnection connection);

        IReadOnlyList<RpcClientCallInterceptor> GetCallInterceptors();

        TService GetServiceInstance<TService>(RpcObjectRef serviceRef, SynchronizationContext? syncContext) where TService : class;

        TService GetServiceSingleton<TService>(RpcServerConnectionInfo connectionInfo, SynchronizationContext? syncContext) where TService : class;

        IRpcServerConnection GetServerConnection(RpcServerConnectionInfo connectionInfo);

        void RemoveCallInterceptor(RpcClientCallInterceptor callInterceptor);

        bool RemoveKnownConnection(IRpcServerConnection connection);
    }

    public static class RpcServerConnectionManagerExtensions
    {
        public static TService GetServiceInstance<TService>(this IRpcServerConnectionManager connectionManager, RpcObjectRef serviceRef, bool useSyncContext = true) where TService : class
        {
            return connectionManager.GetServiceInstance<TService>(serviceRef, useSyncContext ? SynchronizationContext.Current : null);
        }

        public static TService GetServiceInstance<TService>(this IRpcServerConnectionManager connectionManager, RpcObjectRef serviceRef, SynchronizationContext syncContext) where TService : class
        {
            return connectionManager.GetServiceInstance<TService>(serviceRef, syncContext);
        }

        public static TService GetServiceInstance<TService>(this IRpcServerConnectionManager connectionManager, RpcObjectRef<TService> serviceRef, SynchronizationContext syncContext) where TService : class
        {
            return connectionManager.GetServiceInstance<TService>(serviceRef, syncContext);
        }

        public static TService GetServiceInstance<TService>(this IRpcServerConnectionManager connectionManager, RpcObjectRef<TService> serviceRef, bool useSyncContext = true) where TService : class
        {
            return connectionManager.GetServiceInstance<TService>(serviceRef, useSyncContext ? SynchronizationContext.Current : null);
        }

        public static TService GetServiceSingleton<TService>(this IRpcServerConnectionManager connectionManager, RpcServerConnectionInfo connectionInfo, bool useSyncContext = true) where TService : class
        {
            return connectionManager.GetServiceSingleton<TService>(connectionInfo, useSyncContext ? SynchronizationContext.Current : null);
        }

        public static TService GetServiceSingleton<TService>(this IRpcServerConnectionManager connectionManager, RpcSingletonRef<TService> singletonRef, bool useSyncContext = true) where TService : class
        {
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

        public static TService GetServiceSingleton<TService>(this IRpcServerConnectionManager connectionManager, RpcSingletonRef<TService> singletonRef, SynchronizationContext syncContext) where TService : class
        {
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
