using NUnit.Framework;
using System;

namespace SciTech.Rpc.Tests.Lightweight
{
    [TestFixtureSource(nameof(DefaultLightweightClientHostFixtureArgs))]
    public class LightweightClientServerTests : ClientServerTests
    {
        protected static readonly object[] DefaultLightweightClientHostFixtureArgs = {
            new object[] { new ProtobufSerializer(), RpcConnectionType.LightweightInproc},
            new object[] { new ProtobufSerializer(), RpcConnectionType.LightweightTcp},
            new object[] { new DataContractGrpcSerializer(), RpcConnectionType.LightweightTcp},
            new object[] { new DataContractGrpcSerializer(), RpcConnectionType.LightweightInproc},
        };

        public LightweightClientServerTests(IRpcSerializer serializer, RpcConnectionType connectionType) :
            base(serializer, connectionType)
        {
        }
    }
}
