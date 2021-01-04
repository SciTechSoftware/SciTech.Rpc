using System;

namespace SciTech.Rpc.Server
{
    public interface IRpcServerExceptionConverter
    {
        string FaultCode { get; }

        Type? FaultDetailsType { get; }

        RpcFaultException? TryCreateFault(Exception exception);
    }
}
