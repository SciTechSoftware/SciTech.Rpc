using NUnit.Framework;
using System;
using System.Linq;

namespace SciTech.Rpc.Tests.NetGrpc
{
#if NET
    [TestFixture]
    public class NetGrpcCancellationTests : CancellationTests
    {
        public NetGrpcCancellationTests() : base(RpcConnectionType.NetGrpc)
        {
        }
    }
#endif
}
