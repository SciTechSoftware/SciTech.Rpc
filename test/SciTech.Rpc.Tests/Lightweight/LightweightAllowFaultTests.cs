using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Lightweight.Client;
using SciTech.Rpc.Lightweight.Client.Internal;
using SciTech.Rpc.Lightweight.Server.Internal;
using SciTech.Rpc.Server.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Lightweight
{
    public class LightweightAllowFaultTests : AllowFaultTests<LightweightMethodDef>
    {
        public LightweightAllowFaultTests() 
            : base(new LightweightProxyTestAdapter(), new LightweightStubTestAdapter())
        {
        }
    }
}
