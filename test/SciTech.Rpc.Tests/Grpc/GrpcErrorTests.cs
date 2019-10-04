using NUnit.Framework;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server;
using SciTech.Rpc.Tests;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Grpc
{
    [TestFixtureSource(nameof(GrpcCommunicationErrorFixtureArgs))]
    public class GrpcErrorTests : RpcErrorsBaseTests
    {
        protected static readonly object[] GrpcCommunicationErrorFixtureArgs = {
            new object[] { new ProtobufRpcSerializer(), RpcConnectionType.Grpc},
        };

        public GrpcErrorTests(IRpcSerializer serializer, RpcConnectionType connectionType) : base(serializer, connectionType)
        {
        }


    }
}
