using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace SciTech.Rpc
{
#pragma warning disable CA2237 // Mark ISerializable types with serializable
    /// <summary>
    /// The exception that is throw when there is an error in the definition of an RPC service interface.
    /// </summary>
    public class RpcDefinitionException : Exception
#pragma warning restore CA2237 // Mark ISerializable types with serializable
    {
        /// <summary>Initializes a new instance of the <see cref="RpcCommunicationException"></see> class.</summary>
        public RpcDefinitionException()
        {

        }

        /// <summary>Initializes a new instance of the <see cref="RpcCommunicationException"></see> class with a specified error message.</summary>
        /// <param name="message">The message that describes the error.</param>
        public RpcDefinitionException(string message) : base(message)
        {

        }

        /// <summary>Initializes a new instance of the <see cref="RpcCommunicationException"></see> class with a specified error message and a reference to the inner exception that is the cause of this exception.</summary>
        /// <param name="message">The error message that explains the reason for the exception.</param>
        /// <param name="innerException">The exception that is the cause of the current exception, or a null reference (Nothing in Visual Basic) if no inner exception is specified.</param>
        public RpcDefinitionException(string message, Exception innerException) : base(message, innerException)
        {

        }
    }
}
