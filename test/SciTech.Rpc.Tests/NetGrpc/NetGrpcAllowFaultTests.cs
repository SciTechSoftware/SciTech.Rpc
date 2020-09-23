#if PLAT_NET_GRPC
using SciTech.Rpc.NetGrpc.Client.Internal;
using System;
using System.Linq;

namespace SciTech.Rpc.Tests.NetGrpc
{

    public class NetGrpcAllowFaultTests : AllowFaultTests<GrpcProxyMethod>
    {
        public NetGrpcAllowFaultTests() : base(new NetGrpcProxyTestAdapter(), new NetGrpcStubTestAdapter())
        {
        }
    }
}
#endif
