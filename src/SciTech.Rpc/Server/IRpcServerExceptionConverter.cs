using System;

namespace SciTech.Rpc.Server
{
    public interface IRpcServerExceptionConverter
    {
        Type ExceptionType { get; }

        string FaultCode { get; }

        Type? FaultDetailsType { get; }

        RpcFaultException? CreateFault(Exception exception);
    }
}
