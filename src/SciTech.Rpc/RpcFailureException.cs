using System;

namespace SciTech.Rpc
{
    public enum RpcFailure
    {
        Unknown,
        SizeLimitExceeded,
        ServiceNotPublished,
        RemoteDefinitionError,
        InvalidData
    }

#pragma warning disable CA2237 // Mark ISerializable types with serializable
#pragma warning disable CA1032 // Implement standard exception constructors
    public class RpcFailureException : Exception
#pragma warning restore CA1032 // Implement standard exception constructors
#pragma warning restore CA2237 // Mark ISerializable types with serializable
    {
        /// <summary>Initializes a new instance of the <see cref="RpcFailureException"></see> class.</summary>
        public RpcFailureException(RpcFailure failure)
        {
            this.Failure = failure;
        }

        /// <summary>Initializes a new instance of the <see cref="RpcFailureException"></see> class with a specified error message.</summary>
        /// <param name="message">The message that describes the error.</param>
        public RpcFailureException(RpcFailure failure, string message) : base(message)
        {
            this.Failure = failure;

        }

        /// <summary>Initializes a new instance of the <see cref="RpcFailureException"></see> class with a specified error message and a reference to the inner exception that is the cause of this exception.</summary>
        /// <param name="message">The error message that explains the reason for the exception.</param>
        /// <param name="innerException">The exception that is the cause of the current exception, or a null reference (Nothing in Visual Basic) if no inner exception is specified.</param>
        public RpcFailureException(RpcFailure failure, string message, Exception innerException) : base(message, innerException)
        {
            this.Failure = failure;
        }

        public RpcFailure Failure { get; }

        internal static RpcFailure GetFailureFromFaultCode(string? faultCode)
        {
            switch (faultCode)
            {
                case "RemoteDefinitionError":
                    return RpcFailure.RemoteDefinitionError;
                case "ServiceNotPublished":
                    return RpcFailure.ServiceNotPublished;
                case "SizeLimitExceeded":
                    return RpcFailure.SizeLimitExceeded;
            }

            return RpcFailure.Unknown;
        }
    }
}
