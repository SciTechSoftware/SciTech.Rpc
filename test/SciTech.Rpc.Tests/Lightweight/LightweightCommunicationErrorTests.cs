using NUnit.Framework;
using SciTech.Rpc.Serialization;
using System;
using System.Collections.Generic;
using System.Text;

namespace SciTech.Rpc.Tests.Lightweight
{
    [TestFixtureSource(nameof(LightweightCommunicationErrorFixtureArgs))]
    public class LightweightCommunicationErrorTests : RpcErrorsBaseTests
    {
        protected static readonly object[] LightweightCommunicationErrorFixtureArgs = {
            new object[] { new ProtobufRpcSerializer(), RpcConnectionType.LightweightTcp},
            new object[] { new JsonRpcSerializer(), RpcConnectionType.LightweightTcp},    
        };

        public LightweightCommunicationErrorTests(IRpcSerializer serializer, RpcConnectionType connectionType) : base(serializer, connectionType)
        {
        }
    }
}
