using NUnit.Framework;
using System;
using System.Linq;

namespace SciTech.Rpc.Tests.Grpc
{
    [TestFixture]
    public class GrpcCancellationTests : CancellationTests
    {
        public GrpcCancellationTests() : base(RpcConnectionType.Grpc, false)
        {
        }
    }
}
