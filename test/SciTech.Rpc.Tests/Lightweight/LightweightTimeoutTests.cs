using NUnit.Framework;
using SciTech.Rpc.Client;
using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Server;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Lightweight
{
    [TestFixture(RpcConnectionType.LightweightTcp, true)]
    [TestFixture(RpcConnectionType.LightweightTcp, false)]
    [TestFixture(RpcConnectionType.LightweightSslTcp, false)]
    [TestFixture(RpcConnectionType.LightweightInproc, true)]
    [TestFixture(RpcConnectionType.LightweightInproc, false)]
    public class LightweightTimeoutTests : TimeoutTests
    {
        public LightweightTimeoutTests(RpcConnectionType connectionType, bool roundTripTimeout) : base(connectionType)//, roundTripTimeout)
        {
            this.RoundTripTimeout = roundTripTimeout;
        }

        public bool RoundTripTimeout { get; }

        [SetUp]
        public void SetupTest()
        {
            RpcProxyOptions.RoundTripCancellationsAndTimeouts = this.RoundTripTimeout;

        }

        [TearDown]
        public void TearDownTest()
        {
            RpcProxyOptions.RoundTripCancellationsAndTimeouts = false;
        }
    }
}
