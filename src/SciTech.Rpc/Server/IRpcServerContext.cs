#region Copyright notice and license

// Copyright (c) 2019-2021, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License.
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//

#endregion Copyright notice and license

using System.Security.Principal;

namespace SciTech.Rpc.Server
{
    /// <summary>
    /// Represents the server side call context. Adds information about the remote user making the call.
    /// </summary>
    public interface IRpcServerContext : IRpcContext
    {
        /// <summary>
        /// Gets the remote user associated with the call. Currently only available when using a Lightweight server with negotiate authentication,
        /// or if assigned by a custom call interceptor.
        /// </summary>
        /// <value>The remote user associated with the call</value>
        IPrincipal? User { get; }
    }

    /// <summary>
    /// Allows modification of a server side call context. A <see cref="IRpcServerContextBuilder"/> is provided to server side call interceptors that can modify the
    /// context provided to the call handler.
    /// </summary>
    /// <seealso cref="RpcServerCallInterceptor"/> 
    /// <seealso cref="IRpcServerOptions"/> 
    public interface IRpcServerContextBuilder : IRpcServerContext
    {
        /// <summary>
        /// Gets or sets the remote user associated with the call. 
        /// </summary>
        /// <value>The remote user associated with the call</value>
        new IPrincipal? User { get; set; }
    }
}