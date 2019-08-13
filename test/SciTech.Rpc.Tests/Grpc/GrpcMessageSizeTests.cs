using NUnit.Framework;
using System;
using System.Linq;

namespace SciTech.Rpc.Tests.Grpc
{
    [TestFixtureSource(nameof(DefaultGrpcMessageSizeArgs))]
    public class GrpcMessageSizeTests : MessageSizeTests
    {
        protected static readonly object[] DefaultGrpcMessageSizeArgs = {
            new object[] { new ProtobufSerializer(), RpcConnectionType.Grpc} };

        public GrpcMessageSizeTests(IRpcSerializer serializer, RpcConnectionType connectionType)
            : base(serializer, connectionType)
        {

        }
    }
}
