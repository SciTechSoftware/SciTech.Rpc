using NUnit.Framework;
using SciTech.Rpc.Server;
using System;

namespace SciTech.Rpc.Tests
{
    [TestFixture]
    public class RpcServicePublisherTests
    {
        [Test]
        public void InitConnectionInfo_WithSameServerId_Should_ReplaceConnectionInfo()
        {
            var serverId = RpcServerId.NewId();
            var publisher = new RpcServicePublisher(new RpcServiceDefinitionsBuilder(), serverId);

            // As when an end point is added to a server.
            publisher.TryInitConnectionInfo(new RpcConnectionInfo("127.0.0.1", new Uri("lightweight.tcp://127.0.0.1:1234"), serverId));

            var connectionInfo = new RpcConnectionInfo("Thermostat server", new Uri("lightweight.tcp://thermostat:1234"), serverId);
            publisher.InitConnectionInfo(connectionInfo);

            Assert.AreEqual(connectionInfo, publisher.ConnectionInfo);
            Assert.AreEqual("Thermostat server", publisher.ConnectionInfo.DisplayName);
        }

        [Test]
        public void InitConnectionInfo_WithoutServerId_Should_KeepServerId()
        {
            var serverId = RpcServerId.NewId();
            var publisher = new RpcServicePublisher(new RpcServiceDefinitionsBuilder(), serverId);

            publisher.InitConnectionInfo(new RpcConnectionInfo("Thermostat server", new Uri("lightweight.tcp://thermostat:1234")));

            Assert.AreEqual(serverId, publisher.ConnectionInfo.ServerId);
            Assert.AreEqual("Thermostat server", publisher.ConnectionInfo.DisplayName);
        }

        [Test]
        public void InitConnectionInfo_WithOtherServerId_Should_Throw()
        {
            var publisher = new RpcServicePublisher(new RpcServiceDefinitionsBuilder(), RpcServerId.NewId());

            Assert.Throws<InvalidOperationException>(() => publisher.InitConnectionInfo(
                new RpcConnectionInfo("Thermostat server", new Uri("lightweight.tcp://thermostat:1234"), RpcServerId.NewId())));
        }

        [Test]
        public void InitConnectionInfo_AfterRetrieve_Should_Throw()
        {
            var serverId = RpcServerId.NewId();
            var publisher = new RpcServicePublisher(new RpcServiceDefinitionsBuilder(), serverId);
            publisher.InitConnectionInfo(new RpcConnectionInfo("Thermostat server", new Uri("lightweight.tcp://thermostat:1234"), serverId));

            publisher.RetrieveConnectionInfo();

            // The connection info may already have been provided to clients, e.g. in object references.
            Assert.Throws<InvalidOperationException>(() => publisher.InitConnectionInfo(
                new RpcConnectionInfo("Other name", new Uri("lightweight.tcp://thermostat:1234"), serverId)));
        }
    }
}
