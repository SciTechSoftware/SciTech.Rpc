using NUnit.Framework;
using SciTech.Rpc.Client;
using System;
using System.Linq;

namespace SciTech.Rpc.Tests.Lightweight
{
    [TestFixture(RpcConnectionType.LightweightSslTcp)]
    [TestFixture(RpcConnectionType.LightweightTcp)]
    public class LightweightRpcOptionTests : RpcOptionTests
    {
        public LightweightRpcOptionTests(RpcConnectionType connectionType) : base(connectionType)
        {
        }

        protected override RpcServerConnectionInfo CreateConnectionInfo()
        {
            switch (this.ConnectionType)
            {
                case RpcConnectionType.LightweightTcp:
                case RpcConnectionType.LightweightSslTcp:
                    return new RpcServerConnectionInfo(new Uri("lightweight.tcp://machine"));
                default:
                    throw new InvalidOperationException();
            }
        }

        protected override IRpcConnectionProvider CreateConnectionProvider(ImmutableRpcClientOptions options)
        {
            switch (this.ConnectionType)
            {
                case RpcConnectionType.LightweightTcp:
                    return new Rpc.Lightweight.Client.LightweightConnectionProvider(options);
                case RpcConnectionType.LightweightSslTcp:
                    return new Rpc.Lightweight.Client.LightweightConnectionProvider(TestCertificates.SslClientOptions, options);
                default:
                    throw new InvalidOperationException();
            }
        }
    }
}
