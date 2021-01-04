using NUnit.Framework;
using SciTech.Rpc.Client.Internal;
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
        public bool RoundTripCancellation { get; }

        public LightweightCancellationTests(RpcConnectionType connectionType, bool roundTripCancellation) : base(connectionType)
        {
            this.RoundTripCancellation = roundTripCancellation;
        }

        [SetUp]
        public void SetupTest()
        {
            RpcProxyOptions.RoundTripCancellationsAndTimeouts = this.RoundTripCancellation;

        }

        [TearDown]
        public void TearDownTest()
        {
            RpcProxyOptions.RoundTripCancellationsAndTimeouts = false;
        }
    }
}
