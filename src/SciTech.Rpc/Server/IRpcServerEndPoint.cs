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

namespace SciTech.Rpc.Server
{
    /// <summary>
    /// Represents a connction end point for an RPC server.
    /// </summary>
    public interface IRpcServerEndPoint
    {
        /// <summary>
        /// Gets the display name of this end point. If not explicitle provided, this is usually the same 
        /// as <see cref="HostName"/>.
        /// </summary>
        /// <value>The display name of this end point.</value>
        string DisplayName { get; }

        /// <summary>
        /// Gets the host name of this end point.
        /// </summary>
        /// <value>The display name of this end point.</value>
        string HostName { get; }

        /// <summary>
        /// Gets an <see cref="RpcConnectionInfo"/> that can be used to create a connection to an RPC server using this end point.
        /// </summary>
        /// <param name="serverId">Identifier of the RPC server.</param>
        /// <returns>An <see cref="RpcConnectionInfo"/> that can be used to create a connection to this end point.</returns>
        RpcConnectionInfo GetConnectionInfo(RpcServerId serverId);
    }
}
