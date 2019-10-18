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
using System.Linq;

namespace SciTech.Rpc
{
    /// <summary>
    /// <para>
    /// Represents an RPC call context. Currently only provides access to meta-data header values,
    /// but may be extended in the future.
    /// </para>
    /// <para>The call context can currently only be accessed using client and call server interceptors. It
    /// is not possible to supply or receive it on RPC operation methods.</para>
    /// </summary>
    public interface IRpcCallContext
    {
        /// <summary>
        /// Gets a meta-data header string identified by <paramref name="key"/>.
        /// </summary>
        /// <param name="key">Key identifying the header string.</param>
        /// <returns>The value stored under the key, or <c>null</c> if a value does not exist.</returns>
        string? GetHeaderString(string key);
    }
}
