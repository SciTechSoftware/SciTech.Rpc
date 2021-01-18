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
    public interface IRpcChannel : IAsyncDisposable, IDisposable
    {
        /// <summary>
        /// Gets information about how a connection to the RPC server can be established using this channel.
        /// </summary>
        RpcConnectionInfo ConnectionInfo { get; }

        /// <summary>
        /// Gets the options for this channel.
        /// </summary>
        ImmutableRpcClientOptions Options { get; }

        /// <summary>
        /// Gets a proxy to the service instance specified by <paramref name="objectId"/>.
        /// </summary>
        /// <typeparam name="TService"></typeparam>
        /// <param name="objectId"></param>
        /// <param name="implementedServices">Optional information about the services that 
        /// are implemented on the server side. </param>
        /// <param name="syncContext">Optional synchronization context to use </param>
        /// <returns>The service proxy.</returns>
        /// <exception cref="RpcDefinitionException">Thrown if the RPC definition of <typeparamref name="TService"/> is not correct.</exception>
        TService GetServiceInstance<TService>(RpcObjectId objectId, IReadOnlyCollection<string>? implementedServices, SynchronizationContext? syncContext) where TService : class;

        /// <summary>
        /// Gets a proxy to the service singleton identified by <typeparamref name="TService"/>.
        /// </summary>
        /// <typeparam name="TService">The service contract type.</typeparam>
        /// <param name="syncContext">Optional synchronization context to use </param>
        /// <returns>The service proxy.</returns>
        /// <exception cref="RpcDefinitionException">Thrown if the RPC definition of <typeparamref name="TService"/> is not correct.</exception>
        TService GetServiceSingleton<TService>(SynchronizationContext? syncContext) where TService : class;

        /// <summary>
        /// Disconnects this connection and cleans up any used resources.
        /// </summary>
        /// <returns></returns>
        Task ShutdownAsync();
    }
}
