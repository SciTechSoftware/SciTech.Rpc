using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Lightweight.Client;
using SciTech.Rpc.Lightweight.Client.Internal;
using SciTech.Rpc.Lightweight.Server.Internal;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Lightweight
{
    public class LightweightAllowFaultTests : AllowFaultTests<LightweightMethodDef>
    {
        // Would probably be better to use JsonRpcSerializer as default.
        public static readonly IRpcSerializer serializer = new ProtobufRpcSerializer();

        public LightweightAllowFaultTests() 
            : base(new LightweightProxyTestAdapter(), new LightweightStubTestAdapter(serializer))
        {
        }
    }
}
