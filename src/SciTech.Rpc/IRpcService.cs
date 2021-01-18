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
using SciTech.Threading;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Client
{
    /// <summary>
    /// 
    /// </summary>
    public interface IRpcService : IEquatable<IRpcService>, IDisposable
    {
        /// <summary>
        /// Invoked when the communication has failed for a remote <c>EventHandler</c>. 
        /// Normally this occurs if the connection is lost while an event handler has 
        /// been added to the remote service.
        /// </summary>
        event EventHandler? EventHandlerFailed;

        IRpcChannel Channel { get; }

        RpcObjectId ObjectId { get; }

        SynchronizationContext? SyncContext { get; }

        Task<TService?> TryCastAsync<TService>() where TService : class;

        TService UnsafeCast<TService>() where TService : class;

        Task WaitForPendingEventHandlersAsync();
    }

    public static class RpcServiceExtensions
    {
        public static TService Cast<TService>(this IRpcService rpcService) where TService : class
        {
            var service = rpcService?.TryCastAsync<TService>().Result;
            if (service != null)
            {
                return service;
            }

            throw new InvalidCastException($"Cannot cast RPC service to {typeof(TService)}.");
        }

        public static async Task<TService> CastAsync<TService>(this IRpcService rpcService) where TService : class
        {
            var service = rpcService != null ? await rpcService.TryCastAsync<TService>().ConfigureAwait(false) : null;
            if (service != null)
            {
                return service;
            }

            throw new InvalidCastException($"Cannot cast RPC service to {typeof(TService)}.");
        }

        public static TService SetSyncContext<TService>(this TService rpcService, SynchronizationContext? syncContext) where TService : class, IRpcService
        {
            if (rpcService is RpcProxyBase proxyBase)
            {
                return proxyBase.Channel.GetServiceInstance<TService>(proxyBase.ObjectId, proxyBase.ImplementedServices, syncContext);
            }

            throw new ArgumentException("Can only set synchronization context on services retrieved using IRpcConnection.");
        }

        public static TService? TryCast<TService>(this IRpcService rpcService) where TService : class
        {
            return rpcService?.TryCastAsync<TService>()?.AwaiterResult();
        }

        public static bool TryCast<TService>(this IRpcService rpcService, out TService? service) where TService : class
        {
            service = rpcService?.TryCastAsync<TService>()?.AwaiterResult();
            return service != null;
        }
    }
}
