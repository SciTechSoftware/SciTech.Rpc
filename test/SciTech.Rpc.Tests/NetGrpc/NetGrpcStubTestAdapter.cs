using SciTech.Rpc.Internal;
using SciTech.Rpc.NetGrpc.Server.Internal;
using SciTech.Rpc.Server.Internal;
using SciTech.Rpc.Tests.Grpc;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciTech.Rpc.Tests.NetGrpc
{
    internal class NetGrpcStubTestAdapter : IStubTestAdapter
    {
        public IReadOnlyList<object> GenerateMethodStubs<TService>(IRpcServerImpl rpcServer) where TService : class
        {
            var builder = new NetGrpcServiceStubBuilder<TService>(RpcBuilderUtil.GetServiceInfoFromType(typeof(TService)), null);

            var binder = new TestNetGrpcMethodBinder<TService>();
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
