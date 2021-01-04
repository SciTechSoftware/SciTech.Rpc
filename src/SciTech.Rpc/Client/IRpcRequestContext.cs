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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace SciTech.Rpc.Client
{
    /// <summary>
    /// Extends the <see cref="IRpcContext"/> with the possibility to add meta data header 
    /// values. Can be used by client call interceptors to supply metadata with an RPC operation.
    /// </summary>
    public interface IRpcRequestContext : IRpcContext
    {
        /// <summary>
        /// Adds a meta data header string under the specified <paramref name="key"/>.
        /// </summary>
        /// <param name="key">Key identifying the header string.</param>
        /// <param name="value">The header string to add.</param>
        void AddHeader(string key, string value);

        void AddBinaryHeader(string key, IReadOnlyList<byte> value);

    }
   
}
