using NUnit.Framework;
using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Lightweight
{
    [TestFixture(RpcConnectionType.LightweightTcp, true)]
    [TestFixture(RpcConnectionType.LightweightTcp, false)]
    [TestFixture(RpcConnectionType.LightweightInproc, true)]
    [TestFixture(RpcConnectionType.LightweightInproc, false)]
    public class LightweightCallbackTests : CallbackTests
    {

        public LightweightCallbackTests(RpcConnectionType connectionType, bool roundTripCancellation) : base(new ProtobufRpcSerializer(), connectionType, roundTripCancellation)
        {        
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
