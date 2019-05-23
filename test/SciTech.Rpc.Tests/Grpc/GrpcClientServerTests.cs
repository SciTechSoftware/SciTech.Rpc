using NUnit.Framework;
using SciTech.Rpc.Tests;
using System;
using System.Collections.Generic;
using System.Text;

namespace SciTech.Rpc.Grpc.Tests
{
    [TestFixtureSource(nameof(DefaultGrpsClientHostFixtureArgs))]
    public class GrpcClientServerTests : ClientServerTests
    {
        public GrpcClientServerTests(IRpcSerializer serializer, RpcConnectionType connectionType) :
            base(serializer, connectionType)
        {
        }

        protected static readonly object[] DefaultGrpsClientHostFixtureArgs = {
            new object[] { new ProtobufSerializer(), RpcConnectionType.Grpc},
            new object[] { new DataContractGrpcSerializer(), RpcConnectionType.Grpc},
        };

    }
}
