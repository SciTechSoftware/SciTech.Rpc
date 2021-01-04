#if PLAT_NET_GRPC
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.NetGrpc
{
    public class NetGrpcActivationTests : ActivationTests
    {
        public NetGrpcActivationTests() : base(RpcConnectionType.NetGrpc)
        {

        }
    }
}
#endif