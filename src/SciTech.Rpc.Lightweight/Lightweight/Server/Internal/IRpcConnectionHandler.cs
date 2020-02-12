#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using SciTech.Rpc.Lightweight.Internal;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server.Internal
{
    /// <summary>
    /// Represents a handler that is used by <see cref="ILightweightRpcListener"/>
    /// when a pipeline connection has been established, or a datagram packet received.
    /// </summary>
    public interface IRpcConnectionHandler
    {
        /// <summary>
        /// Should be called by listener when a connection to an pipeline has been established.
        /// This method will read requests and write responses to the pipeline, until the <paramref name="cancellationToken"/>
        /// is cancelled, or the <paramref name="clientPipe"/> is closed.
        /// </summary>
        /// <param name="clientPipe"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task RunPipelineClientAsync(IDuplexPipe clientPipe, LightweightRpcEndPoint endPoint, CancellationToken cancellationToken);

        /// <summary>
        /// Handles a single datagram packet and returns a datagram response.
        /// </summary>
        ValueTask<byte[]?> HandleDatagramAsync(LightweightRpcEndPoint endPoint, byte[] data, CancellationToken cancellationToken);
    }
}
