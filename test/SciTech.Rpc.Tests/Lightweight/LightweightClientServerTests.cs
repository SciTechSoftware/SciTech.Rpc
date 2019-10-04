using NUnit.Framework;
using SciTech.Rpc.Serialization;
using System;

namespace SciTech.Rpc.Tests.Lightweight
{
    [TestFixtureSource(nameof(DefaultLightweightClientHostFixtureArgs))]
    public class LightweightClientServerTests : ClientServerBaseTests
    {
        protected internal static readonly object[] DefaultLightweightClientHostFixtureArgs = {
            new object[] { new ProtobufRpcSerializer(), RpcConnectionType.LightweightNamedPipe},
            new object[] { new ProtobufRpcSerializer(), RpcConnectionType.LightweightInproc},
            new object[] { new ProtobufRpcSerializer(), RpcConnectionType.LightweightTcp},
            new object[] { new JsonRpcSerializer(), RpcConnectionType.LightweightTcp},
            new object[] { new ProtobufRpcSerializer(), RpcConnectionType.LightweightSslTcp},
            new object[] { new DataContractRpcSerializer(), RpcConnectionType.LightweightTcp},
            new object[] { new DataContractRpcSerializer(), RpcConnectionType.LightweightInproc},
        };

        public LightweightClientServerTests(IRpcSerializer serializer, RpcConnectionType connectionType) :
            base(serializer, connectionType)
        {
        }
    }
}
