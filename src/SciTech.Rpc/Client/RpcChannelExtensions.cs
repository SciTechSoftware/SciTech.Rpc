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
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace SciTech.Rpc.Client
{
    public static class RpcChannelExtensions
    {
        /// <summary>
        /// Gets a proxy to the service instance specified by <paramref name="objectId"/>.
        /// </summary>
        /// <typeparam name="TService"></typeparam>
        /// <param name="objectId"></param>
        /// <param name="useSyncContext">Indicates that the current synchronization context should be used for the service intance.</param>
        /// <returns>The service proxy.</returns>
        /// <exception cref="RpcDefinitionException">Thrown if the RPC definition of <typeparamref name="TService"/> is not correct.</exception>
        public static TService GetServiceInstance<TService>(this IRpcChannel connection, RpcObjectId objectId, bool useSyncContext = true) where TService : class
        {
            if (connection is null) throw new ArgumentNullException(nameof(connection));

            return connection.GetServiceInstance<TService>(objectId, default, useSyncContext ? SynchronizationContext.Current : null);
        }

        /// <summary>
        /// Gets a proxy to the service instance specified by <paramref name="objectId"/>.
        /// </summary>
        /// <typeparam name="TService"></typeparam>
        /// <param name="objectId"></param>
        /// <param name="syncContext">Optional synchronization context to use.</param>
        /// <returns>The service proxy.</returns>
        /// <exception cref="RpcDefinitionException">Thrown if the RPC definition of <typeparamref name="TService"/> is not correct.</exception>
        public static TService GetServiceInstance<TService>(this IRpcChannel connection, RpcObjectId objectId, SynchronizationContext? syncContext ) where TService : class
        {
            if (connection is null) throw new ArgumentNullException(nameof(connection));

            return connection.GetServiceInstance<TService>(objectId, default, syncContext );
        }

        /// <summary>
        /// Gets a proxy to the service instance specified by <paramref name="serviceRef"/>.
        /// </summary>
        /// <typeparam name="TService"></typeparam>
        /// <param name="serviceRef">An RPC object ref identifying the service instance.</param>
        /// <param name="useSyncContext">Indicates that the current synchronization context should be used for the service intance.</param>
        /// <returns>The service proxy.</returns>
        /// <exception cref="RpcDefinitionException">Thrown if the RPC definition of <typeparamref name="TService"/> is not correct.</exception>
        public static TService? GetServiceInstance<TService>(this IRpcChannel connection, RpcObjectRef<TService> serviceRef, bool useSyncContext = true) where TService : class
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

        /// <summary>
        /// Gets a proxy to the service instance specified by <paramref name="serviceRef"/>.
        /// </summary>
        /// <typeparam name="TService"></typeparam>
        /// <param name="serviceRef">An RPC object ref identifying the service instance.</param>
        /// <param name="syncContext">Optional synchronization context to use </param>
        /// <returns>The service proxy.</returns>
        /// <exception cref="RpcDefinitionException">Thrown if the RPC definition of <typeparamref name="TService"/> is not correct.</exception>
        public static TService? GetServiceInstance<TService>(this IRpcChannel connection, RpcObjectRef<TService> serviceRef, SynchronizationContext? syncContext) where TService : class
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
