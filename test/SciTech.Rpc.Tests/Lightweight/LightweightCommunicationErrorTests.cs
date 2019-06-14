using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text;

namespace SciTech.Rpc.Tests.Lightweight
{
    [TestFixtureSource(nameof(LightweightCommunicationErrorFixtureArgs))]
    public class LightweightCommunicationErrorTests : RpcErrorsBaseTests
    {
        protected static readonly object[] LightweightCommunicationErrorFixtureArgs = {
            //new object[] { new ProtobufSerializer(), RpcConnectionType.LightweightInproc},
            new object[] { new ProtobufSerializer(), RpcConnectionType.LightweightTcp},
        };

        public LightweightCommunicationErrorTests(IRpcSerializer serializer, RpcConnectionType connectionType) : base(serializer, connectionType)
        {
        }
    }
}
