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
    public interface IRpcServer : IDisposable
    {
        /// <summary>
        /// Indicates that service instances returned from an RPC call is 
        /// allowed to be automatically published.
        /// </summary>
        bool AllowAutoPublish { get; }

        IRpcServicePublisher ServicePublisher { get; }

        /// <summary>
        /// Gets the server id associated with this RPC server.
        /// </summary>
        RpcServerId ServerId { get; }
    }
}
