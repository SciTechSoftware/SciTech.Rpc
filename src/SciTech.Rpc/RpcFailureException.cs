using System;

namespace SciTech.Rpc
{
    /// <summary>
    /// Defines the failure codes that may be reported through an <see cref="RpcFailureException"/>.
    /// </summary>
    public enum RpcFailure
    {
        /// <summary>
        /// Indicates that an unknown failure has occurred.
        /// </summary>
        Unknown,

        /// <summary>
        /// Indicates that an RPC operation has failed due to a message size limitation (i.e.
        /// request or response message is larger than the configured maximum message size).
        /// </summary>
        SizeLimitExceeded,

        /// <summary>
        /// Indicates that a  service object returned from a service method or property has
        /// not been published and auto-publishing is not enabled.
        /// </summary>
        ServiceNotPublished,

        /// <summary>
        /// Indicates an error or mismatch in the server side definition of an RPC service.
        /// </summary>
        RemoteDefinitionError,

        /// <summary>
        /// Indicates that invalid data has been received in an RPC request or response.
        /// </summary>
        InvalidData,

        /// <summary>
        /// Indicates that an end point address is already in use, e.g. that some other service or process is 
        /// listening to the same network interface and port.
        /// </summary>
        AddressInUse
    }

    /// <summary>
    /// The exception that is thrown when an RPC operation fails.
    /// </summary>
    public class RpcFailureException : RpcBaseException
    {
        /// <summary>Initializes a new instance of the <see cref="RpcFailureException"></see> class.</summary>
        public RpcFailureException(RpcFailure failure) : base( $"RPC failure '{failure}' has occured.")
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

        /// <summary>
        /// Gets the code of the RPC failure.
        /// </summary>
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
