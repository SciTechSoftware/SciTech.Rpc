﻿#region Copyright notice and license
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
    public interface IRpcServerConnection : IRpcChannel
    {

        event EventHandler? Connected;

        event EventHandler? ConnectionFailed;

        event EventHandler? ConnectionLost;

        event EventHandler ConnectionStateChanged;

        event EventHandler? Disconnected;


        RpcConnectionState ConnectionState { get; }
    }
}
