using NUnit.Framework;
using System;
using System.Linq;

namespace SciTech.Rpc.Tests.NetGrpc
{
#if NETCOREAPP3_0
    [TestFixture]
    public class NetGrpcCancellationTests : CancellationTests
    {
        public NetGrpcCancellationTests() : base(RpcConnectionType.NetGrpc)
        {
        }
    }
#endif
}
