using NUnit.Framework;
using System;
using System.Linq;

namespace SciTech.Rpc.Tests.Lightweight
{
    [TestFixtureSource(nameof(DefaultLightweightMessageSizeArgs))]
    public class LightweightMessageSizeTests : MessageSizeTests
    {
        protected internal static readonly object[] DefaultLightweightMessageSizeArgs = {
            new object[] { new ProtobufSerializer(), RpcConnectionType.LightweightInproc, true},
            new object[] { new ProtobufSerializer(), RpcConnectionType.LightweightTcp, true},
            new object[] { new ProtobufSerializer(), RpcConnectionType.LightweightTcp, false},
            new object[] { new ProtobufSerializer(), RpcConnectionType.LightweightSslTcp, true},
            new object[] { new ProtobufSerializer(), RpcConnectionType.LightweightSslTcp, false}
        };

        public LightweightMessageSizeTests(IRpcSerializer serializer, RpcConnectionType connectionType, bool keepConnectionAlive)
            : base(serializer, connectionType, keepConnectionAlive)
        {
            this.LightweightOptions = new Rpc.Lightweight.LightweightOptions { KeepSizeLimitedConnectionAlive = keepConnectionAlive };
        }
    }
}
