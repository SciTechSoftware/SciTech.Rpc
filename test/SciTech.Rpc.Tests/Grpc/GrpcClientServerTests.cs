using NUnit.Framework;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Tests;
using System;

namespace SciTech.Rpc.Tests.Grpc
{
    [TestFixtureSource(nameof(DefaultGrpsClientHostFixtureArgs))]
    public class GrpcClientServerTests : ClientServerBaseTests
    {
        protected static readonly object[] DefaultGrpsClientHostFixtureArgs = {
            new object[] { new ProtobufRpcSerializer(), RpcConnectionType.Grpc},
            new object[] { new DataContractRpcSerializer(), RpcConnectionType.Grpc},
            new object[] { new JsonRpcSerializer(), RpcConnectionType.Grpc},
        };

        public GrpcClientServerTests(IRpcSerializer serializer, RpcConnectionType connectionType) :
            base(serializer, connectionType)
        {
        }
    }
}
