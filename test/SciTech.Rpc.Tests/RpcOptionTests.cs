using NUnit.Framework;
using SciTech.Rpc.Client;
using System;
using System.IO;
using System.Linq;

namespace SciTech.Rpc.Tests
{
    public abstract class RpcOptionTests
    {
        protected static readonly RpcClientOptions ConnectionManagerOptions = new RpcClientOptions
        {
            CallTimeout = TimeSpan.FromSeconds(1),
            ReceiveMaxMessageSize = 1000,
            SendMaxMessageSize = 2000,
            StreamingCallTimeout = TimeSpan.FromSeconds(2),
            Serializer = new DummySerializer(1)
        };

        protected static readonly RpcClientOptions ProviderOptions = new RpcClientOptions
        {
            CallTimeout = TimeSpan.FromSeconds(2),
            ReceiveMaxMessageSize = 2000,
            SendMaxMessageSize = 3000,
            StreamingCallTimeout = TimeSpan.FromSeconds(3),
            Serializer = new DummySerializer(2)
        };

        protected readonly RpcConnectionType ConnectionType;

        protected RpcOptionTests(RpcConnectionType connectionType)
        {
            this.ConnectionType = connectionType;

        }

        [Test]
        public void ConnectionManagerOptions_Should_Propagate()
        {
            var connectionManager = new RpcServerConnectionManager(new IRpcConnectionProvider[] { this.CreateConnectionProvider(null) }, ConnectionManagerOptions);

            var connection = (RpcServerConnection)connectionManager.GetServerConnection(this.CreateConnectionInfo());

            AssertOptions(ConnectionManagerOptions, connection.Options);
        }

        [Test]
        public void ConnectionProviderOptions_Should_Propagate()
        {
            var connectionManager = new RpcServerConnectionManager(new IRpcConnectionProvider[] { this.CreateConnectionProvider(ProviderOptions.AsImmutable()) }, ConnectionManagerOptions);

            var connection = (RpcServerConnection)connectionManager.GetServerConnection(this.CreateConnectionInfo());

            AssertOptions(ProviderOptions, connection.Options);
        }

        protected abstract RpcServerConnectionInfo CreateConnectionInfo();

        protected abstract IRpcConnectionProvider CreateConnectionProvider(ImmutableRpcClientOptions options);

        private static void AssertOptions(RpcClientOptions options, ImmutableRpcClientOptions actualOptions)
        {
            Assert.AreEqual(options.CallTimeout, actualOptions.CallTimeout);
            Assert.AreEqual(options.ReceiveMaxMessageSize, actualOptions.ReceiveMaxMessageSize);
            Assert.AreEqual(options.SendMaxMessageSize, actualOptions.SendMaxMessageSize);
            Assert.AreEqual(options.StreamingCallTimeout, actualOptions.StreamingCallTimeout);
            Assert.AreEqual(options.Serializer, actualOptions.Serializer);
        }

        private class DummySerializer : IRpcSerializer
        {
            internal readonly int Id;

            internal DummySerializer(int id)
            {
                this.Id = id;
            }

            public object FromStream(Type type, Stream input)
            {
                throw new NotImplementedException();
            }

            public void ToStream(Stream stream, object input)
            {
                throw new NotImplementedException();
            }
        }
    }
}
