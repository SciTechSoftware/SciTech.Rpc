using NUnit.Framework;
using SciTech.Rpc.Tests;
using System;

namespace SciTech.Rpc.Grpc.Tests
{
    [TestFixtureSource(nameof(DefaultGrpsClientHostFixtureArgs))]
    public class GrpcClientServerTests : ClientServerBaseTests
    {
        protected static readonly object[] DefaultGrpsClientHostFixtureArgs = {
            new object[] { new ProtobufSerializer(), RpcConnectionType.Grpc},
            new object[] { new DataContractRpcSerializer(), RpcConnectionType.Grpc},
        };

        public GrpcClientServerTests(IRpcSerializer serializer, RpcConnectionType connectionType) :
            base(serializer, connectionType)
        {
        }
    }
}
