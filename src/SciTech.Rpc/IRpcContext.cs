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

using SciTech.Rpc.Server;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace SciTech.Rpc
{
    /// <summary>
    /// <para>
    /// Represents an RPC call context. 
    /// </para>
    /// <para>The call context can currently only be accessed using client/server call interceptors and through <see cref="IRpcContextAccessor"/> on the server side. It
    /// is not possible to supply or receive it on RPC operation methods.</para>
    /// </summary>
    public interface IRpcContext
    {      
        /// <summary>
        /// Gets a meta-data header string identified by <paramref name="key"/>.
        /// </summary>
        /// <param name="key">Key identifying the header string.</param>
        /// <returns>The value stored under the key, or <c>null</c> if a value does not exist.</returns>
        string? GetHeaderString(string key);

        /// <summary>
        /// Gets a binary meta-data header identified by <paramref name="key"/>.
        /// </summary>
        /// <param name="key">Key identifying the header string.</param>
        /// <returns>The value stored under the key, or <c>default</c> if a value does not exist.</returns>
        ImmutableArray<byte> GetBinaryHeader(string key);

        /// <summary>
        /// Gets the cancellation token associated with the current operation. It is used to signal that the operation is cancelled.
        /// </summary>
        /// <value>Cancellation token that is used to signal that the operation is cancelled</value>
        CancellationToken CancellationToken { get; }
    }
}
