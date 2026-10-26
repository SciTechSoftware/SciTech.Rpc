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


using SciTech.Rpc.Client;
using SciTech.Rpc.Server;

namespace SciTech.Rpc.Lightweight
{
    /// <summary>
    /// Extends <see cref="RpcClientOptions"/> and <see cref="RpcServerOptions"/> with "Lightweight" specific
    /// options.
    /// </summary>
    public class LightweightOptions
    {
        /// <summary>
        /// If <c>true</c>, indicates that a lightweight connection should be kept alive even if the size of a message
        /// exceeds the limits specified by <see cref="RpcClientOptions"/> and <see cref="RpcServerOptions"/>. By default 
        /// this setting is <c>true</c>.
        /// </summary>
        /// <remarks>
        /// Keeping the connection alive requires the full message to be read, even if it will be directly discarded 
        /// and not stored in memory. If the connection is not kept alive, then other active method calls will fail,
        /// including streaming calls and active event handlers.
        /// </remarks>
        public bool? KeepSizeLimitedConnectionAlive { get; set; }

        /// <summary>
        /// If <c>true</c> indicates that it is allowed for a connection to reconnect to the server if possible. By defaut
        /// this setting is <c>false</c>.
        /// </summary>
        public bool? AllowReconnect { get; set; }

        /// <summary>
        /// If <c>true</c>, indicates that operation numbers may be used instead of operation names in request and
        /// response frames, if supported by the other side of the connection. This reduces the size of frames. By default
        /// this setting is <c>true</c>.
        /// </summary>
        /// <remarks>
        /// Set this to <c>false</c> on the client or the server to always include the operation name in the frames, e.g.
        /// to make it easier to identify frames when analysing network traffic. Disabling operation numbers on one side
        /// disables them for the connection.
        /// </remarks>
        public bool? UseOperationNumbers { get; set; }
    }
}
