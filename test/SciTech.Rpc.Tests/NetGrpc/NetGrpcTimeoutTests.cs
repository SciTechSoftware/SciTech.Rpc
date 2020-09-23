using NUnit.Framework;
using SciTech.Rpc.Client;
using SciTech.Rpc.Server;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.NetGrpc
{
#if PLAT_NET_GRPC
    public class NetGrpcTimeoutTests : TimeoutTests
    {
        public NetGrpcTimeoutTests() : base(RpcConnectionType.NetGrpc)//, roundTripTimeout)
        {
        }
    }
#endif
}
