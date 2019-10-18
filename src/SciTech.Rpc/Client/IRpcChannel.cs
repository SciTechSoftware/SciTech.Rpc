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
using System.Threading.Tasks;

namespace SciTech.Rpc.Client
{
    /// <summary>
    /// Defines methods and properties for a communication channel to an RPC server, e.g. for retrieving proxies to
    /// remote services. 
    /// </summary>
    public interface IRpcChannel : IDisposable
    {
        /// <summary>
        /// Gets information about how a connection to the RPC server can be established using this channel.
        /// </summary>
        RpcServerConnectionInfo ConnectionInfo { get; }

        /// <summary>
        /// Gets the options for this channel.
        /// </summary>
        ImmutableRpcClientOptions Options { get; }

        TService GetServiceInstance<TService>(RpcObjectId objectId, IReadOnlyCollection<string>? implementedServices, SynchronizationContext? syncContext) where TService : class;

        TService GetServiceSingleton<TService>(SynchronizationContext? syncContext) where TService : class;

        Task ShutdownAsync();
    }
}
