﻿using NUnit.Framework;
using System;

namespace SciTech.Rpc.Tests.Lightweight
{
    [TestFixtureSource(nameof(DefaultLightweightClientHostFixtureArgs))]
    public class LightweightClientServerTests : ClientServerBaseTests
    {
        protected internal static readonly object[] DefaultLightweightClientHostFixtureArgs = {
            new object[] { new ProtobufSerializer(), RpcConnectionType.LightweightNamedPipe},
            new object[] { new ProtobufSerializer(), RpcConnectionType.LightweightInproc},
            new object[] { new ProtobufSerializer(), RpcConnectionType.LightweightTcp},
            new object[] { new ProtobufSerializer(), RpcConnectionType.LightweightSslTcp},
            new object[] { new DataContractRpcSerializer(), RpcConnectionType.LightweightTcp},
            new object[] { new DataContractRpcSerializer(), RpcConnectionType.LightweightInproc},
        };

        public LightweightClientServerTests(IRpcSerializer serializer, RpcConnectionType connectionType) :
            base(serializer, connectionType)
        {
        }
    }
}
