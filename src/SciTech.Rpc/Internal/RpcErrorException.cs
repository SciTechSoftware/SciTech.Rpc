#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//

//
// Based on SimplPipeline in SimplPipelines/SimplSockets by Marc Gravell (https://github.com/mgravell/simplsockets)
//
#endregion

using System;
using System.Collections.Immutable;

namespace SciTech.Rpc.Internal
{
    /// <summary>
    /// Internal exception class that is used to propagate <see cref="RpcError"/>s to a suitable error handler.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "Internal exception propagation")]
    internal class RpcErrorException : Exception 
    {
        internal RpcErrorException(RpcError error)
            : base( error.Message )
        {
            this.Error = error;
        }
        
        internal RpcError Error { get; }
    }
}
