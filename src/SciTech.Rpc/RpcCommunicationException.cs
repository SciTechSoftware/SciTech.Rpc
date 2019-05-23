﻿using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Security;
using System.Text;

namespace SciTech.Rpc
{
    /// <summary>
    /// The exception that is thrown when a communication error occurs with an RPC server. This 
    /// usually indicates a transient error such as an unreachable host, or other network problems.
    /// </summary>
#pragma warning disable CA2237 // Mark ISerializable types with serializable
#pragma warning disable CA1032 // Implement standard exception constructors
    public class RpcCommunicationException : Exception
    {
        public RpcCommunicationStatus Status { get; }

        /// <summary>Initializes a new instance of the <see cref="RpcCommunicationException"></see> class.</summary>
        public RpcCommunicationException(RpcCommunicationStatus status) : this( status, "", null)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="RpcCommunicationException"></see> class with a specified error message.</summary>
        /// <param name="message">The message that describes the error.</param>
        public RpcCommunicationException(RpcCommunicationStatus status, string message) : this(status, message, null)
        {

        }

        /// <summary>Initializes a new instance of the <see cref="RpcCommunicationException"></see> class with a specified error message and a reference to the inner exception that is the cause of this exception.</summary>
        /// <param name="message">The error message that explains the reason for the exception.</param>
        /// <param name="innerException">The exception that is the cause of the current exception, or a null reference (Nothing in Visual Basic) if no inner exception is specified.</param>
        public RpcCommunicationException(RpcCommunicationStatus status, string message, Exception? innerException) : base(message, innerException)
        {
            this.Status = status;
        }
    }
#pragma warning restore CA1032 // Implement standard exception constructors
#pragma warning restore CA2237 // Mark ISerializable types with serializable

    public enum RpcCommunicationStatus
    {
        None,
        Unavailable,
        Disconnected,
        ConnectionLost,
        Unknown
    }
}
