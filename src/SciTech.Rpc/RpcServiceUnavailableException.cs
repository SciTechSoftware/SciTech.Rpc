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

namespace SciTech.Rpc
{
    /// <summary>
    /// The exception that is thrown when a service singleton or instance cannot be reached because it is not available on the server.
    /// </summary>
    public class RpcServiceUnavailableException : RpcBaseException
    {
        public RpcServiceUnavailableException() : base("RPC service is not available.")
        {
        }

        public RpcServiceUnavailableException(string message) : base(message)
        {
        }

        public RpcServiceUnavailableException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
