using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text;

namespace SciTech.Rpc.Tests.Pipelines
{
    [TestFixtureSource(nameof(PipelinesCommunicationErrorFixtureArgs))]
    public class PipelinesCommunicationErrorTests : RpcErrorsBaseTests
    {
        protected static readonly object[] PipelinesCommunicationErrorFixtureArgs = {
            //new object[] { new ProtobufSerializer(), RpcConnectionType.InprocPipelines},
            new object[] { new ProtobufSerializer(), RpcConnectionType.TcpPipelines},
        };

        public PipelinesCommunicationErrorTests(IRpcSerializer serializer, RpcConnectionType connectionType) : base(serializer, connectionType)
        {
        }
    }
}
