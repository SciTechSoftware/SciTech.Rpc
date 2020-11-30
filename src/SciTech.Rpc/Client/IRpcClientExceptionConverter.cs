using System;

namespace SciTech.Rpc.Client
{
    /// <summary>
    /// Allows an implementer to convert from an RPC fault to a custom exception.
    /// </summary>
    public interface IRpcClientExceptionConverter
    {
        ////Type ExceptionType { get; }

        ///// <summary>
        ///// Gets the code of the fault that this converter can handle.
        ///// </summary>
        //string FaultCode { get; }

        ///// <summary>
        ///// Gets the type of the fault details used by this converter, or <c>null</c> if 
        ///// no fault details are needed.
        ///// </summary>
        //Type? FaultDetailsType { get; }

        ///// <summary>
        ///// Creates a custom exception based on <see cref="FaultCode"/>, <paramref name="message"/>
        ///// and the optional <paramref name="details"/>.
        ///// <para>
        ///// </summary>
        ///// <param name="message">Message text as provided by the RPC fault.</param>
        ///// <param name="details">Optional details. Will always be provided if <see cref="FaultDetailsType"/> is not <c>null</c>.</param>
        ///// <returns></returns>
        //Exception CreateException(string message, object? details);

        Exception? TryCreateException(RpcFaultException faultException);
    }
}
