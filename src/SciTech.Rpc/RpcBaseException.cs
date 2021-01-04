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
