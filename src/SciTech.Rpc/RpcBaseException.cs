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

using System;
using System.Collections.Generic;
using System.Text;

namespace SciTech.Rpc
{
    /// <summary>
    /// Base class for RPC exceptions that may occur during an RPC operation.
    /// </summary>
    public abstract class RpcBaseException : Exception
    {
        protected RpcBaseException(string message) : base(message)
        {
        }

        protected RpcBaseException(string message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
