using SciTech.Rpc.Grpc.Client.Internal;
using System;
using System.Linq;

namespace SciTech.Rpc.Tests.Grpc
{
    public class GrpcAllowFaultTests : AllowFaultTests<GrpcProxyMethod>
    {
        public GrpcAllowFaultTests() : base(new GrpcProxyTestAdapter(), new GrpcStubTestAdapter())
        {
        }
    }
}
