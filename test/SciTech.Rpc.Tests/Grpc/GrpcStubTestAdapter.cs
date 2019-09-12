using SciTech.Rpc.Internal;
using SciTech.Rpc.Grpc.Server.Internal;
using SciTech.Rpc.Server.Internal;
using SciTech.Rpc.Tests.Grpc;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciTech.Rpc.Tests.Grpc
{
    internal class GrpcStubTestAdapter : IStubTestAdapter
    {
        public IReadOnlyList<object> GenerateMethodStubs<TService>(IRpcServerImpl rpcServer) where TService : class
        {
            var builder = new GrpcServiceStubBuilder<TService>(RpcBuilderUtil.GetServiceInfoFromType(typeof(TService)), null);

            var binder = new TestGrpcMethodBinder();
            builder.GenerateOperationHandlers(rpcServer, binder);

            return binder.stubs;
        }


        public object GetMethodStub(IReadOnlyList<object> stubs, string methodName)
        {
            return stubs.Cast<TestGrpcMethodStub>().FirstOrDefault(m => $"{m.Method.ServiceName}.{m.Method.Name}" == methodName);
        }

        public Type GetRequestType(object method)
        {
            return ((TestGrpcMethodStub)method).RequestType;
        }

        public Type GetResponseType(object method)
        {
            return ((TestGrpcMethodStub)method).ResponseType;
        }
    }
}
