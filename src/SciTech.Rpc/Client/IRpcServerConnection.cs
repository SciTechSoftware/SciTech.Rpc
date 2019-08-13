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
    public enum RpcConnectionState
    {
        None,
        Connected,
        ConnectionFailed,
        ConnectionLost,
        Disconnected,
    }

    /// <summary>
    /// Defines methods and properties for a server connection, e.g. for retrieving proxies to
    /// remote services. 
    /// </summary>
    /// <remarks>
    /// A server connection represents an established connection to a server process. <see cref="RpcServerConnectionInfo"/> is
    /// used to provide information about a connection.
    /// </remarks>
    public interface IRpcServerConnection
    {
        event EventHandler ConnectionStateChanged;

        RpcServerConnectionInfo ConnectionInfo { get; }

        RpcConnectionState ConnectionState { get; }

        TService GetServiceInstance<TService>(RpcObjectId objectId, IReadOnlyCollection<string>? implementedServices, SynchronizationContext? syncContext) where TService : class;

        TService GetServiceSingleton<TService>(SynchronizationContext? syncContext) where TService : class;

        Task ShutdownAsync();
    }
}
