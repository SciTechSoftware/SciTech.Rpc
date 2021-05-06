using System;

namespace SciTech.Rpc.Server
{
    /// <summary>
    /// Represents a converter that can be used to convert a server side exception to a <see cref="RpcFaultException"/>
    /// that can be propagated to the client.
    /// </summary>
    public interface IRpcServerExceptionConverter
    {
        /// <summary>
        /// Gets the fault code of the <see cref="RpcFaultException"/> that will be created by <see cref="TryCreateFault(Exception)"/>.
        /// </summary>
        string FaultCode { get; }

        /// <summary>
        /// Gets the type of the optional fault details that may be included in in the <see cref="RpcFaultException{TFault}"/>. If no 
        /// fault details will be provided, <c>null</c> is returned.
        /// </summary>
        /// <value>the type of the optional fault details, or  <c>null</c> if no details are included</value>
        Type? FaultDetailsType { get; }

        /// <summary>
        /// Tries to create an <see cref="RpcFaultException"/> for the provided <paramref name="exception"/>. If this
        /// converter cannot handle the exception, <c>null</c> is returned.
        /// </summary>
        /// <param name="exception">Exception that should be converted</param>
        /// <returns>The converted exception as an <see cref="RpcFaultException"/>, or <c>null</c> if the exception cannot be converted.</returns>
        RpcFaultException? TryCreateFault(Exception exception);
    }
}
