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
using System.Threading.Tasks;

namespace SciTech.Rpc.Server
{
    /// <summary>
    /// Extends <see cref="IRpcServer"/> with methods and properties related to a self-hosted server. 
    /// </summary>
    public interface IRpcServerHost : IRpcServer
    {
        /// <summary>
        /// Adds an end point to the host. 
        /// </summary>
        /// <param name="endPoint">An end point that can be used to connect to this host.</param>
        void AddEndPoint(IRpcServerEndPoint endPoint);

        /// <summary>
        /// Shuts down the host. After shutdown the host will stop responding to new RPC calls and all end points will be closed.
        /// </summary>
        Task ShutdownAsync();

        /// <summary>
        /// Starts the host.
        /// </summary>
        void Start();
    }
}
