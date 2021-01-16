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
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace SciTech.Rpc.Server
{
    /// <summary>
    /// Provides base methods and properties for a SciTech RPC server.
    /// </summary>
    public interface IRpcServer : IAsyncDisposable, IDisposable
    {
        /// <summary>
        /// Indicates that service instances returned from an RPC call is 
        /// allowed to be automatically published.
        /// </summary>
        bool AllowAutoPublish { get; }

        /// <summary>
        /// Gets the RPC service publisher that is used to publish services on this server.
        /// <para>Normally it is only necessary to use this interface when publishing the same set of services on
        /// multiple <see cref="IRpcServer"/>s. The <see cref="RpcServerExtensions"/> class provides extensions methods
        /// that can be used to publish RPC services directly on an <see cref="IRpcServer"/> interface.
        /// </para>        
        /// </summary>
        IRpcServicePublisher ServicePublisher { get; }

        /// <summary>
        /// Gets the server id associated with this RPC server.
        /// </summary>
        RpcServerId ServerId { get; }
    }
}
