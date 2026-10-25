#if NET
using NUnit.Framework;
using SciTech.Rpc.Client;
using SciTech.Rpc.NetGrpc.Client;
using System;

namespace SciTech.Rpc.Tests.NetGrpc
{
    [TestFixture]
    public class NetGrpcRpcOptionTests : RpcOptionTests
    {
        public NetGrpcRpcOptionTests() : base(RpcConnectionType.NetGrpc)
        {
        }

        protected override RpcConnectionInfo CreateConnectionInfo()
        {
            return new RpcConnectionInfo(new Uri("grpc://machine"));
        }

        protected override IRpcConnectionProvider CreateConnectionProvider(ImmutableRpcClientOptions options)
        {
            return new NetGrpcConnectionProvider(options);
        }
    }
}
#endif
