#if NET
using NUnit.Framework;
using SciTech.Rpc.Serialization;

namespace SciTech.Rpc.Tests.NetGrpc
{
    [TestFixtureSource(nameof(NetGrpcCommunicationErrorFixtureArgs))]
    public class NetGrpcErrorTests : RpcErrorsBaseTests
    {
        protected static readonly object[] NetGrpcCommunicationErrorFixtureArgs = {
            new object[] { new ProtobufRpcSerializer(), RpcConnectionType.NetGrpc},
        };

        public NetGrpcErrorTests(IRpcSerializer serializer, RpcConnectionType connectionType) : base(serializer, connectionType)
        {
        }
    }
}
#endif
