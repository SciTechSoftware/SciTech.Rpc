using SciTech.Rpc.Server.Internal;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciTech.Rpc.Tests
{
    public interface IStubTestAdapter
    {
        IReadOnlyList<object> GenerateMethodStubs<TService>(IRpcServerImpl rpcServer) where TService : class;

        object GetMethodStub(IReadOnlyList<object> stub, string methodName);

        Type GetRequestType(object method);

        Type GetResponseType(object method);
    }
}
