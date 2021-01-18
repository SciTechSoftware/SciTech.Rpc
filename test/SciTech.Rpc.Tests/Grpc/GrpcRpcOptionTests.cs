using NUnit.Framework;
using SciTech.Rpc.Client;
using SciTech.Rpc.Grpc.Client;
using System;
using System.Linq;

namespace SciTech.Rpc.Tests.Grpc
{
    [TestFixture]
    public class GrpcRpcOptionTests : RpcOptionTests
    {
        public GrpcRpcOptionTests() : base(RpcConnectionType.Grpc)
        {
        }

        protected override RpcConnectionInfo CreateConnectionInfo()
        {
            return new RpcConnectionInfo(new Uri("grpc://machine"));
        }

        protected override IRpcConnectionProvider CreateConnectionProvider(ImmutableRpcClientOptions options)
        {
            return new GrpcConnectionProvider(options);
        }
    }
}
