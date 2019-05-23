using NUnit.Framework;
using System;

namespace SciTech.Rpc.Tests.Pipelines
{
    [TestFixtureSource(nameof(DefaultPipelinesClientHostFixtureArgs))]
    public class PipelinesClientServerTests : ClientServerTests
    {
        protected static readonly object[] DefaultPipelinesClientHostFixtureArgs = {
            new object[] { new ProtobufSerializer(), RpcConnectionType.InprocPipelines},
            new object[] { new ProtobufSerializer(), RpcConnectionType.TcpPipelines},
            new object[] { new DataContractGrpcSerializer(), RpcConnectionType.TcpPipelines},
            new object[] { new DataContractGrpcSerializer(), RpcConnectionType.InprocPipelines},
        };

        public PipelinesClientServerTests(IRpcSerializer serializer, RpcConnectionType connectionType) :
            base(serializer, connectionType)
        {
        }
    }
}
