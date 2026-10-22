#if NET
using NUnit.Framework;
using SciTech.Rpc.Serialization;

namespace SciTech.Rpc.Tests.NetGrpc
{
    [TestFixtureSource(nameof(DefaultNetGrpcMessageSizeArgs))]
    public class NetGrpcMessageSizeTests : MessageSizeTests
    {
        protected static readonly object[] DefaultNetGrpcMessageSizeArgs = {
            new object[] { new ProtobufRpcSerializer(), RpcConnectionType.NetGrpc} };

        public NetGrpcMessageSizeTests(IRpcSerializer serializer, RpcConnectionType connectionType)
            : base(serializer, connectionType)
        {
        }
    }
}
#endif
