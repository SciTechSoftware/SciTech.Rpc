using SciTech.Rpc.Lightweight.Server.Internal;
using SciTech.Rpc.Server.Internal;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciTech.Rpc.Tests.Lightweight
{
    internal class LightweightStubTestAdapter : IStubTestAdapter
    {
        public IReadOnlyList<object> GenerateMethodStubs<TService>(IRpcServerImpl rpcServer) where TService : class
        {
            var builder = new LightweightServiceStubBuilder<INoFaultService>(null);

            var binder = new TestLightweightMethodBinder();
            builder.GenerateOperationHandlers(rpcServer, binder);

            return binder.methods;
        }


        public object GetMethodStub(IReadOnlyList<object> stubs, string methodName)
        {
            return stubs.Cast<LightweightMethodStub>().FirstOrDefault(m => m.OperationName == methodName);
        }

        public Type GetRequestType(object method)
        {
            return ((LightweightMethodStub)method).RequestType;
        }

        public Type GetResponseType(object method)
        {
            return ((LightweightMethodStub)method).ResponseType;
        }
    }
}
