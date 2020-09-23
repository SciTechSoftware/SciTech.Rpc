using NUnit.Framework;
using System;
using System.Linq;

namespace SciTech.Rpc.Tests.NetGrpc
{
#if PLAT_NET_GRPC
    [TestFixture]
    public class NetGrpcCancellationTests : CancellationTests
    {
        public NetGrpcCancellationTests() : base(RpcConnectionType.NetGrpc)
        {
        }
    }
#endif
}
