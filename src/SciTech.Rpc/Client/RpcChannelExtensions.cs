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

using SciTech.Rpc.Internal;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

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
        /// <exception cref="RpcFailureException">>Thrown if the server id of the <paramref name="connection"/> 
        /// does not match the server id of the <paramref name="serviceRef"/></exception>
        [return: NotNullIfNotNull("serviceRef")]
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

            throw new RpcFailureException(RpcFailure.ServerMismatch, "Invalid serviceRef connection.");
        }

        /// <summary>
        /// Gets a proxy to the service instance specified by <paramref name="serviceRef"/>.
        /// </summary>
        /// <typeparam name="TService"></typeparam>
        /// <param name="serviceRef">An RPC object ref identifying the service instance.</param>
        /// <param name="syncContext">Optional synchronization context to use </param>
        /// <returns>The service proxy.</returns>
        /// <exception cref="RpcDefinitionException">Thrown if the RPC definition of <typeparamref name="TService"/> is not correct.</exception>
        /// <exception cref="RpcFailureException">>Thrown if the server id of the <paramref name="connection"/> 
        /// does not match the server id of the <paramref name="serviceRef"/></exception>
        [return: NotNullIfNotNull("serviceRef")]
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

            throw new RpcFailureException(RpcFailure.ServerMismatch, "Invalid serviceRef connection.");
        }

        /// <exception cref="RpcFailureException">>Thrown if the server id of the <paramref name="connection"/>
        /// does not match the server id of the <paramref name="serviceRef"/></exception>
        [return: NotNullIfNotNull(nameof(serviceRef))]
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

            throw new RpcFailureException(RpcFailure.ServerMismatch, "Invalid serviceRef connection.");
        }

        /// <exception cref="RpcFailureException">>Thrown if the server id of the <paramref name="connection"/> 
        /// does not match the server id of the <paramref name="serviceRef"/></exception>
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

            throw new RpcFailureException(RpcFailure.ServerMismatch, "Invalid serviceRef connection.");
        }

        public static TService GetServiceSingleton<TService>(this IRpcChannel connection, bool useSyncContext = true) where TService : class
        {
            if (connection is null) throw new ArgumentNullException(nameof(connection));

            return connection.GetServiceSingleton<TService>(useSyncContext ? SynchronizationContext.Current : null);
        }

        /// <exception cref="RpcFailureException">>Thrown if the server id of the <paramref name="connection"/> 
        /// does not match the server id of the <paramref name="singletonRef"/></exception>
        public static TService GetServiceSingleton<TService>(this IRpcChannel connection, RpcSingletonRef<TService> singletonRef, bool useSyncContext = true) where TService : class
        {
            if (connection is null) throw new ArgumentNullException(nameof(connection));

            if (singletonRef?.ServerConnection?.Matches(connection.ConnectionInfo) == true)
            {
                return connection.GetServiceSingleton<TService>(useSyncContext ? SynchronizationContext.Current : null);
            }

            throw new RpcFailureException(RpcFailure.ServerMismatch, "Invalid serviceRef connection.");
        }

        /// <summary>
        /// Retrieves information about the server of <paramref name="connection"/>: its id, display name and properties,
        /// and the singleton services it publishes. Use it to check whether a server is compatible after connecting,
        /// e.g. by host name or address, without discovery.
        /// </summary>
        /// <remarks>Servers older than SciTech.Rpc 1.0 don't implement the operation, and then the call fails like a call to
        /// any unknown operation.</remarks>
        public static async Task<RpcServerInfo> GetServerInfoAsync(this IRpcChannel connection, CancellationToken cancellationToken = default)
        {
            if (connection is null) throw new ArgumentNullException(nameof(connection));

            // The proxy must not capture the synchronization context of the caller.
            var service = connection.GetServiceSingleton<IRpcServerInfoService>((SynchronizationContext?)null);
            var response = await service.GetServerInfoAsync(cancellationToken).ConfigureAwait(false);

            return RpcServerInfo.FromResponse(response, connection.ConnectionInfo);
        }
    }
}
