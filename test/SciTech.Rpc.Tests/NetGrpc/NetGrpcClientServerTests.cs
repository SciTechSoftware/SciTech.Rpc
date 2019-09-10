﻿using NUnit.Framework;
using SciTech.Rpc.Tests;
using System;

namespace SciTech.Rpc.NetGrpc.Tests
{
#if NETCOREAPP3_0
    [TestFixtureSource(nameof(DefaultGrpcClientHostFixtureArgs))]
    public class NetGrpcClientServerTests : ClientServerBaseTests
    {
        protected static readonly object[] DefaultGrpcClientHostFixtureArgs = {
            new object[] { new ProtobufSerializer(), RpcConnectionType.NetGrpc}
        };

        public NetGrpcClientServerTests(IRpcSerializer serializer, RpcConnectionType connectionType) :
            base(serializer, connectionType)
        {
        }
    }
#endif
}
