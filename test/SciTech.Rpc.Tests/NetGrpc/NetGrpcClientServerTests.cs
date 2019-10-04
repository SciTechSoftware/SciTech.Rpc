using NUnit.Framework;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Tests;
using System;

namespace SciTech.Rpc.Tests.NetGrpc
{
#if NETCOREAPP3_0
    [TestFixtureSource(nameof(DefaultGrpcClientHostFixtureArgs))]
    public class NetGrpcClientServerTests : ClientServerBaseTests
    {
        protected static readonly object[] DefaultGrpcClientHostFixtureArgs = {
            new object[] { new ProtobufRpcSerializer(), RpcConnectionType.NetGrpc},
            new object[] { new JsonRpcSerializer(), RpcConnectionType.NetGrpc}
        };

        public NetGrpcClientServerTests(IRpcSerializer serializer, RpcConnectionType connectionType) :
            base(serializer, connectionType)
        {
        }
    }
#endif
}
