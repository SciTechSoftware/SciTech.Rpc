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

using SciTech.Rpc.Client.Internal;
using SciTech.Threading;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Client
{
    /// <summary>
    /// Represents a proxy to an RPC service. Will be implemented by all service proxies retrieved from an <see cref="IRpcChannel"/>.
    /// </summary>
    public interface IRpcProxy : IEquatable<IRpcProxy>, IDisposable
    {
        /// <summary>
        /// Invoked when the communication has failed for a remote <c>EventHandler</c>. 
        /// Normally this occurs if the connection is lost while an event handler has 
        /// been added to the remote service.
        /// <para>It will also occur if the handler or the <see cref="SyncContext"/> throws an exception.</para>
        /// <para><note type="note">This event will be invoked in the thread that detected the exception. It will not be 
        /// marshalled by the <see cref="SyncContext"/> (in case the error was caused by the synchronization context).</note></para>
        /// </summary>
        event EventHandler<ExceptionEventArgs>? EventHandlerFailed;

        IRpcChannel Channel { get; }

        RpcObjectId ObjectId { get; }

        SynchronizationContext? SyncContext { get; }

        /// <summary>
        /// Tries to cast this service proxy to a <typeparamref name="TService"/> proxy. If knowledge about implemented service
        /// interfaces is not available in this proxy, this method can cause a round-trip to the server to retrieve this information.
        /// <para><note type="note">To avoid the round-trip, the <see cref="UnsafeCast{TService}"/> method can be used</note></para>
        /// </summary>
        /// <typeparam name="TService">The type of the service interface.</typeparam>
        /// <returns>A service proxy for the requested <typeparamref name="TService"/> interface.</returns>
        Task<TService?> TryCastAsync<TService>() where TService : class;

        /// <summary>
        /// Casts this service proxy to a <typeparamref name="TService"/> proxy, without verifying that the remote service
        /// implements the <typeparamref name="TService"/> interface.
        /// <para><note type="note">This can save a round-trip to the server, but this method should only be used 
        /// if it is known that the service is implemented on the server.</note></para>
        /// </summary>
        /// <typeparam name="TService">The type of the service interface.</typeparam>
        /// <returns>A service proxy for the requested <typeparamref name="TService"/> interface.</returns>
        TService UnsafeCast<TService>() where TService : class;

        /// <summary>
        /// Wait for all added eventh handlers to be acknowledged.
        /// <para>When adding </para>
        /// </summary>
        /// <returns></returns>
        Task WaitForPendingEventHandlersAsync();
    }

    public static class RpcServiceExtensions
    {
        public static TService Cast<TService>(this IRpcProxy rpcService) where TService : class
        {
            var service = rpcService?.TryCastAsync<TService>().Result;
            if (service != null)
            {
                return service;
            }

            throw new InvalidCastException($"Cannot cast RPC service to {typeof(TService)}.");
        }

        public static async Task<TService> CastAsync<TService>(this IRpcProxy rpcService) where TService : class
        {
            var service = rpcService != null ? await rpcService.TryCastAsync<TService>().ConfigureAwait(false) : null;
            if (service != null)
            {
                return service;
            }

            throw new InvalidCastException($"Cannot cast RPC service to {typeof(TService)}.");
        }

        public static TService SetSyncContext<TService>(this TService rpcService, SynchronizationContext? syncContext) where TService : class, IRpcProxy
        {
            if (rpcService is RpcProxyBase proxyBase)
            {
                return proxyBase.Channel.GetServiceInstance<TService>(proxyBase.ObjectId, proxyBase.ImplementedServices, syncContext);
            }


            throw new ArgumentException("Can only set synchronization context on services retrieved using IRpcConnection.");
        }

        public static TService? TryCast<TService>(this IRpcProxy rpcService) where TService : class
        {
            return rpcService?.TryCastAsync<TService>()?.AwaiterResult();
        }

        public static bool TryCast<TService>(this IRpcProxy rpcService, out TService? service) where TService : class
        {
            service = rpcService?.TryCastAsync<TService>()?.AwaiterResult();
            return service != null;
        }
    }
}
