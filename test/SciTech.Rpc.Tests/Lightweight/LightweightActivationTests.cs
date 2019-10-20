using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Lightweight
{

    [TestFixture(RpcConnectionType.LightweightNamedPipe)]
    public class LightweightActivationTests :ActivationTests
    {
        public LightweightActivationTests(RpcConnectionType connectionType) : base(connectionType)
        {
        }
    }
}
