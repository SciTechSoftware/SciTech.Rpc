using System;

namespace SciTech.Rpc.Client
{
    public interface IRpcClientExceptionConverter
    {
        Type ExceptionType { get; }

        string FaultCode { get; }

        Type? FaultDetailsType { get; }

        Exception CreateException(string message, object? details);
    }
}
