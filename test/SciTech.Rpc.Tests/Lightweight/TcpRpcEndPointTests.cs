using NUnit.Framework;
using SciTech.Rpc.Client;
using SciTech.Rpc.Lightweight.Client;
using SciTech.Rpc.Lightweight.Server;
using SciTech.Rpc.Server;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Lightweight
{
    [TestFixture]
    public class TcpRpcEndPointTests
    {
        [Test]
        public void HostNameEndPoint_Should_PreferIPv4Address()
        {
            // "localhost" resolves to ::1 first on Windows.
            var endPoint = new TcpRpcEndPoint("localhost", ClientServerTestsBase.TcpTestPort, false);

            Assert.AreEqual(IPAddress.Loopback, endPoint.EndPoint.Address);
        }

        [Test]
        public void BindToAllInterfaces_Should_ListenOnAnyAddress()
        {
            var endPoint = new TcpRpcEndPoint("localhost", ClientServerTestsBase.TcpTestPort, true);

            var expectedAddress = Socket.OSSupportsIPv6 ? IPAddress.IPv6Any : IPAddress.Any;
            Assert.AreEqual(expectedAddress, endPoint.EndPoint.Address);
        }

        [Test]
        public async Task HostNameEndPoint_Should_BeReachableByHostName()
        {
            // As in the README: a client connecting to "localhost" must reach a server listening on "localhost".
            var server = new LightweightRpcServer();
            server.AddEndPoint(new TcpRpcEndPoint("localhost", ClientServerTestsBase.TcpTestPort, false));
            server.PublishSingleton<ISimpleService>(new TestSimpleServiceImpl());
            server.Start();
            try
            {
                await using var connection = new TcpRpcConnection(new RpcConnectionInfo(new Uri($"lightweight.tcp://localhost:{ClientServerTestsBase.TcpTestPort}")));
                var service = connection.GetServiceSingleton<ISimpleService>();

                Assert.AreEqual(3, await service.AddAsync(1, 2).DefaultTimeout());
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }
    }
}
