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
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace SciTech.Rpc.Client
{
    public static class RpcChannelExtensions
    {
        public static TService GetServiceInstance<TService>(this IRpcChannel connection, RpcObjectId objectId, bool useSyncContext = true) where TService : class
        {
            if (connection is null) throw new ArgumentNullException(nameof(connection));

            return connection.GetServiceInstance<TService>(objectId, default, useSyncContext ? SynchronizationContext.Current : null);
        }

        public static TService? GetServiceInstance<TService>(this IRpcChannel connection, RpcObjectRef<TService> serviceRef, bool useSyncContext = true) where TService : class
        {
            if (connection is null) throw new ArgumentNullException(nameof(connection));

            if (serviceRef == null)
            {
                return null;
            }

            if (connection != null && serviceRef.ServerConnection?.Matches(connection.ConnectionInfo) == true)
            {
                return connection.GetServiceInstance<TService>(serviceRef.ObjectId, serviceRef.ImplementedServices, useSyncContext ? SynchronizationContext.Current : null);
            }

            throw new ArgumentException("Invalid serviceRef connection.");
        }

        public static TService? GetServiceInstance<TService>(this IRpcChannel connection, RpcObjectRef<TService> serviceRef, SynchronizationContext syncContext) where TService : class
        {
            if (connection is null) throw new ArgumentNullException(nameof(connection));

            if (serviceRef == null)
            {
                return null;
            }

            if (serviceRef.ServerConnection?.Matches(connection.ConnectionInfo) == true)
            {
                return connection.GetServiceInstance<TService>(serviceRef.ObjectId, serviceRef.ImplementedServices, syncContext);
            }

            throw new ArgumentException("Invalid serviceRef connection.");
        }

        [return: NotNullIfNotNull("serviceRef")]
        public static TService? GetServiceInstance<TService>(this IRpcChannel connection, RpcObjectRef serviceRef, bool useSyncContext = true) where TService : class
        {
            if (connection is null) throw new ArgumentNullException(nameof(connection));

            if (serviceRef == null)
            {
                return null;
            }

            if (serviceRef.ServerConnection?.Matches(connection.ConnectionInfo) == true)
            {
                return connection.GetServiceInstance<TService>(serviceRef.ObjectId, serviceRef.ImplementedServices, useSyncContext ? SynchronizationContext.Current : null);
            }

            throw new ArgumentException("Invalid serviceRef connection.");
        }

        public static TService? GetServiceInstance<TService>(this IRpcChannel connection, RpcObjectRef serviceRef, SynchronizationContext? syncContext) where TService : class
        {
            if (connection is null) throw new ArgumentNullException(nameof(connection));

            if (serviceRef == null)
            {
                return null;
            }

            if (serviceRef.ServerConnection?.Matches(connection.ConnectionInfo) == true)
            {
                return connection.GetServiceInstance<TService>(serviceRef.ObjectId, serviceRef.ImplementedServices, syncContext);
            }

            throw new ArgumentException("Invalid serviceRef connection.");
        }

        public static TService GetServiceSingleton<TService>(this IRpcChannel connection, bool useSyncContext = true) where TService : class
        {
            if (connection is null) throw new ArgumentNullException(nameof(connection));

            return connection.GetServiceSingleton<TService>(useSyncContext ? SynchronizationContext.Current : null);
        }

        public static TService GetServiceSingleton<TService>(this IRpcChannel connection, RpcSingletonRef<TService> singletonRef, bool useSyncContext = true) where TService : class
        {
            if (connection is null) throw new ArgumentNullException(nameof(connection));

            if (singletonRef?.ServerConnection?.Matches(connection.ConnectionInfo) == true)
            {
                return connection.GetServiceSingleton<TService>(useSyncContext ? SynchronizationContext.Current : null);
            }

            throw new ArgumentException("Invalid serviceRef connection.");
        }
    }
}
