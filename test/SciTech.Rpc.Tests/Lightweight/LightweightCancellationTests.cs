using NUnit.Framework;
using System;
using System.Linq;

namespace SciTech.Rpc.Tests.Lightweight
{
    [TestFixture(RpcConnectionType.LightweightTcp, true)]
    [TestFixture(RpcConnectionType.LightweightTcp, false)]
    [TestFixture(RpcConnectionType.LightweightSslTcp, false)]
    [TestFixture(RpcConnectionType.LightweightInproc, true)]
    [TestFixture(RpcConnectionType.LightweightInproc, false)]
    public class LightweightCancellationTests : CancellationTests
    {
        public LightweightCancellationTests(RpcConnectionType connectionType, bool roundTripCancellation) : base(connectionType, roundTripCancellation)
        {
        }
    }
}
