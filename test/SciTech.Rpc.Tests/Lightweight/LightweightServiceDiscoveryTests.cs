using Microsoft.Extensions.Logging;
using NUnit.Framework;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Lightweight.Client;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Lightweight.Server;
using SciTech.Rpc.Lightweight.Server.Internal;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Lightweight
{
    [TestFixture]
    public class LightweightServiceDiscoveryTests
    {
        private ILoggerFactory loggerFactory;

        private IRpcSerializer serializer;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            this.loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            this.serializer = new JsonRpcSerializer();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            this.loggerFactory?.Dispose();
            this.loggerFactory = null;

            this.serializer = null;
        }


        [Test]
        public async Task NoSingletons_ShouldReturn_ConnectionInfo()
        { 
            var server = new LightweightRpcServer();
            var connectionInfo = new RpcConnectionInfo(new Uri("test://test"));
            var endPoint = new TestDiscoveryEndPoint(connectionInfo);
            server.AddEndPoint(endPoint);
            server.Start();
            try
            {
                var clientId = Guid.NewGuid();
                var listener = endPoint.Listeners.First();
                var response = await listener.SendReceiveDatagramAsync<RpcDiscoveryRequest, RpcPublishedSingletonsResponse>(
                    ServiceDiscoveryOperations.GetPublishedSingletons,
                    new RpcDiscoveryRequest(clientId),
                    this.serializer);

                Assert.NotNull(response);
                Assert.AreEqual(clientId, response.ClientId);
                Assert.AreEqual(endPoint.GetConnectionInfo(server.ServicePublisher.ServerId), response.ConnectionInfo);
                Assert.AreEqual(0, response.Services.Length);
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }

        [Test]
        public async Task Singletons_ShouldReturn_Published()
        {
            var server = new LightweightRpcServer();
            var connectionInfo = new RpcConnectionInfo(new Uri("test://test"));
            var endPoint = new TestDiscoveryEndPoint(connectionInfo);
            server.AddEndPoint(endPoint);

            server.PublishSingleton<ISimpleService>();
            server.PublishSingleton<ISimpleService2>();

            server.Start();
            try
            {
                var clientId = Guid.NewGuid();
                var listener = endPoint.Listeners.First();
                var response = await listener.SendReceiveDatagramAsync<RpcDiscoveryRequest, RpcPublishedSingletonsResponse>(
                    ServiceDiscoveryOperations.GetPublishedSingletons,
                    new RpcDiscoveryRequest(clientId),
                    this.serializer);

                Assert.NotNull(response);
                Assert.AreEqual(clientId, response.ClientId);
                Assert.AreEqual(2, response.Services.Length);
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }

        [Test]
        public async Task GetConnections_ShouldReturn_Connection()
        {
            var server = new LightweightRpcServer();
            var connectionInfo = new RpcConnectionInfo(new Uri("test://test"));
            var endPoint = new TestDiscoveryEndPoint(connectionInfo);
            server.AddEndPoint(endPoint);

            server.PublishSingleton<ISimpleService>();
            server.PublishSingleton<ISimpleService2>();

            server.Start();
            try
            {
                var clientId = Guid.NewGuid();
                var listener = endPoint.Listeners.First();
                var response = await listener.SendReceiveDatagramAsync<RpcDiscoveryRequest, RpcConnectionInfoResponse>(
                    ServiceDiscoveryOperations.GetConnectionInfo,
                    new RpcDiscoveryRequest(clientId),
                    this.serializer);

                Assert.NotNull(response);
                Assert.AreEqual(clientId, response.ClientId);
                Assert.AreEqual(endPoint.GetConnectionInfo(server.ServicePublisher.ServerId), response.ConnectionInfo);
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }


        [Test]
        public async Task ServerEndPoint_Should_CreateStartAndStop_Listener()
        {
            var server = new LightweightRpcServer();
            var connectionInfo = new RpcConnectionInfo(new Uri("test://test"));
            var endPoint = new TestDiscoveryEndPoint(connectionInfo);
            server.AddEndPoint(endPoint);

            server.Start();

            TestDiscoveryListener listener = endPoint.Listeners.FirstOrDefault();
            Assert.NotNull(listener);

            Assert.IsTrue(listener.IsListening);
            await server.ShutdownAsync();

            Assert.IsTrue(listener.IsStopped);

            server.Dispose();
            Assert.IsTrue(listener.IsDisposed);
        }

        [Test]
        public async Task Multicast_Singletons_ShouldReturn_Published()
        {
            var server = new LightweightRpcServer();
            var connectionInfo = new RpcConnectionInfo(new Uri("test://test"));
            var endPoint = new LightweightDiscoveryEndPoint(connectionInfo);
            server.AddEndPoint(endPoint);

            server.PublishSingleton<ISimpleService>();
            server.PublishSingleton<ISimpleService2>();

            server.Start();
            try
            {
                var responses = await this.MulticastGetPublishedSingletonsAsync(server.ServerId);

                Assert.AreEqual(2, responses[server.ServerId].Services.Length);
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }

        [Test]
        public async Task DualServer_Multicast_Singletons_ShouldBothReturn_Published()
        {
            var server = new LightweightRpcServer();
            var server2 = new LightweightRpcServer();
            var connectionInfo = new RpcConnectionInfo(new Uri("test://test"));
            var endPoint = new LightweightDiscoveryEndPoint(connectionInfo);
            server.AddEndPoint(endPoint);
            server2.AddEndPoint(endPoint);

            server.PublishSingleton<ISimpleService>();
            server2.PublishSingleton<ISimpleService2>();

            server.Start();
            server2.Start();
            try
            {
                var responses = await this.MulticastGetPublishedSingletonsAsync(server.ServerId, server2.ServerId);

                Assert.AreEqual(1, responses[server.ServerId].Services.Length);
                Assert.AreEqual(1, responses[server2.ServerId].Services.Length);
            }
            finally
            {
                await server.ShutdownAsync();
                await server2.ShutdownAsync();
            }
        }

        [Test]
        public async Task DiscoveryClient_BeforeServerStart_Should_Discover()
        {
            var server = new LightweightRpcServer();
            var endPoint = new TcpRpcEndPoint("localhost", "127.0.0.1", ClientServerTestsBase.TcpTestPort, false);
            var discoveryEndPoint = new LightweightDiscoveryEndPoint(endPoint.GetConnectionInfo(server.ServerId));
            server.AddEndPoint(endPoint);
            server.AddEndPoint(discoveryEndPoint);

            server.PublishSingleton<ISimpleService>();
            server.PublishSingleton<ISimpleService2>();

            var discoveryClient = new LightweightDiscoveryClient(loggerFactory.CreateLogger<LightweightDiscoveryClient>());

            // Other discovery servers may be running on the network, so only consider services from the test server.
            bool IsTestService(DiscoveredService service) => service.ConnectionInfo.ServerId == server.ServerId;

            var eventDiscoveredServices = new List<DiscoveredService>();
            bool serviceLost = false;
            discoveryClient.ServiceDiscovered += (s, e) => { if (IsTestService(e.DiscoveredService)) eventDiscoveredServices.Add(e.DiscoveredService); };
            discoveryClient.ServiceLost += (s, e) => { if (IsTestService(e.DiscoveredService)) serviceLost = true; };

            using var discoveryCts = new CancellationTokenSource();
            var discoveryTask = discoveryClient.FindServicesAsync(discoveryCts.Token);
            try
            {
                await Task.Delay(100);

                server.Start();
                try
                {
                    await Task.Delay(2000);

                    Assert.IsFalse(serviceLost);
                    var discoveredServices = discoveryClient.DiscoveredServices.Where(IsTestService).ToList();
                    Assert.AreEqual(2, discoveredServices.Count);
                    Assert.AreEqual(2, eventDiscoveredServices.Count);

                }
                finally
                {

                    await server.ShutdownAsync();
                }
            }
            finally
            {
                discoveryCts.Cancel();
                await discoveryTask.DefaultTimeout();
            }
        }

        [Test]
        public async Task DualServer_DiscoveryClient_BeforeServerStart_Should_Discover()
        {
            var server = new LightweightRpcServer();
            var endPoint = new TcpRpcEndPoint("localhost", "127.0.0.1", ClientServerTestsBase.TcpTestPort, false);
            var discoveryEndPoint = new LightweightDiscoveryEndPoint(endPoint.GetConnectionInfo(server.ServerId));
            server.AddEndPoint(endPoint);
            server.AddEndPoint(discoveryEndPoint);

            var server2 = new LightweightRpcServer();
            var endPoint2 = new TcpRpcEndPoint("localhost", "127.0.0.1", ClientServerTestsBase.TcpTestPort+1, false);
            var discoveryEndPoint2 = new LightweightDiscoveryEndPoint(endPoint2.GetConnectionInfo(server2.ServerId));
            server2.AddEndPoint(endPoint2);
            server2.AddEndPoint(discoveryEndPoint2);

            server.PublishSingleton<ISimpleService>();
            server2.PublishSingleton<ISimpleService2>();

            var discoveryClient = new LightweightDiscoveryClient(loggerFactory.CreateLogger<LightweightDiscoveryClient>());

            // Other discovery servers may be running on the network, so only consider services from the test servers.
            bool IsTestService(DiscoveredService service)
                => service.ConnectionInfo.ServerId == server.ServerId || service.ConnectionInfo.ServerId == server2.ServerId;

            var eventDiscoveredServices = new List<DiscoveredService>();
            bool serviceLost = false;
            discoveryClient.ServiceDiscovered += (s, e) => { if (IsTestService(e.DiscoveredService)) eventDiscoveredServices.Add(e.DiscoveredService); };
            discoveryClient.ServiceLost += (s, e) => { if (IsTestService(e.DiscoveredService)) serviceLost = true; };

            using var discoveryCts = new CancellationTokenSource();
            var discoveryTask = discoveryClient.FindServicesAsync(discoveryCts.Token);
            await Task.Delay(100);

            server.Start();
            server2.Start();
            try
            {
                await Task.Delay(2000);

                Assert.IsFalse(serviceLost);
                var discoveredServices = discoveryClient.DiscoveredServices.Where(IsTestService).ToList();
                Assert.AreEqual(2, discoveredServices.Count);
                Assert.AreEqual(2, eventDiscoveredServices.Count);

                discoveryCts.Cancel();
                await discoveryTask.DefaultTimeout();
            }
            finally
            {
                await server.ShutdownAsync();
                await server2.ShutdownAsync();
            }
        }

        /// <summary>
        /// Multicasts a GetPublishedSingletons request and waits for a response from each of the
        /// specified servers. Responses from other discovery servers on the network, and duplicate
        /// responses (e.g. received on several interfaces), are ignored.
        /// </summary>
        private async Task<Dictionary<RpcServerId, RpcPublishedSingletonsResponse>> MulticastGetPublishedSingletonsAsync(params RpcServerId[] serverIds)
        {
            using var client = new UdpClient(AddressFamily.InterNetwork);
            client.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
            client.JoinMulticastGroup(LightweightDiscoveryEndPoint.DefaultMulticastAddress);

            byte[] requestData = LightweightStubHelper.GetRequestData(ServiceDiscoveryOperations.GetPublishedSingletons, 0, new RpcRequest(), this.serializer);

            // Send the request on each interface the discovery servers listen on. Using only the default
            // multicast interface fails if, e.g., a VPN adapter is preferred.
            var discoveryEp = new IPEndPoint(LightweightDiscoveryEndPoint.DefaultMulticastAddress, LightweightDiscoveryEndPoint.DefaultDiscoveryPort);
            foreach (var interfaceAddress in GetMulticastInterfaceAddresses())
            {
                client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, interfaceAddress.GetAddressBytes());
                var bytesSent = await client.SendAsync(requestData, requestData.Length, discoveryEp);
                Assert.AreEqual(requestData.Length, bytesSent);
            }

            var responses = new Dictionary<RpcServerId, RpcPublishedSingletonsResponse>();
            while (responses.Count < serverIds.Length)
            {
                var receiveRes = await client.ReceiveAsync().DefaultTimeout();
                var response = LightweightStubHelper.GetResponseFromData<RpcPublishedSingletonsResponse>(this.serializer, receiveRes.Buffer);
                var serverId = response?.ConnectionInfo?.ServerId;
                if (serverId is RpcServerId id && Array.IndexOf(serverIds, id) >= 0 && !responses.ContainsKey(id))
                {
                    responses.Add(id, response);
                }
            }

            return responses;
        }

        /// <summary>
        /// Gets the IPv4 address of each interface that <see cref="LightweightDiscoveryEndPoint"/> joins the multicast group on.
        /// </summary>
        private static IEnumerable<IPAddress> GetMulticastInterfaceAddresses()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                    && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback
                    && ni.SupportsMulticast)
                .Select(ni => ni.GetIPProperties())
                .Where(p => p.MulticastAddresses.Count > 0)
                .Select(p => p.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address)
                .Where(a => a != null);
        }
    }

    internal class TestDiscoveryEndPoint : LightweightRpcEndPoint
    {
        RpcConnectionInfo connectionInfo;

        List<TestDiscoveryListener> listeners = new List<TestDiscoveryListener>();

        public TestDiscoveryEndPoint(RpcConnectionInfo connectionInfo)
        {
            this.connectionInfo = connectionInfo;
        }

        public IReadOnlyList<TestDiscoveryListener> Listeners => this.listeners;

        public override string DisplayName => connectionInfo.DisplayName;

        public override string HostName => connectionInfo.HostUrl.Host;

        public override RpcConnectionInfo GetConnectionInfo(RpcServerId serverId)
        {
            return connectionInfo.SetServerId(serverId);
        }

        protected internal override ILightweightRpcListener CreateListener(IRpcConnectionHandler connectionHandler, int maxRequestSize, int maxResponseSize)
        {
            var listener = new TestDiscoveryListener(this, connectionHandler);
            this.listeners.Add(listener);
            return listener;
        }

    }

    internal class TestDiscoveryListener : ILightweightRpcListener
    {
        TestDiscoveryEndPoint endPoint;

        IRpcConnectionHandler connectionHandler;

        public TestDiscoveryListener(TestDiscoveryEndPoint endPoint, IRpcConnectionHandler connectionHandler)
        {
            this.endPoint = endPoint;
            this.connectionHandler = connectionHandler;
        }

        internal bool DisposedWhileListening { get; private set; }

        internal bool IsDisposed { get; private set; }

        internal bool HasListened { get; private set; }

        internal bool IsListening { get; private set; }

        internal bool HasStopped { get; private set; }
        internal bool IsStopped { get; private set; }

        public ValueTask DisposeAsync()
        {
            if (this.IsListening) this.DisposedWhileListening = true;

            this.IsDisposed = true;

            return default;
        }

        public void Listen()
        {
            this.HasListened =  this.IsListening = true;
        }

        public Task StopAsync()
        {
            this.HasStopped = this.IsStopped = true;
            this.IsListening = false;
            return Task.CompletedTask;
        }

        internal Task<TResponse> SendReceiveDatagramAsync<TRequest,TResponse>(string operationName,
            TRequest request,
            IRpcSerializer serializer,
            CancellationToken cancellationToken = default)
            where TRequest : class
            where TResponse : class
        {
            Assert.IsTrue(this.IsListening);

            return LightweightStubHelper.SendReceiveDatagramAsync<TRequest, TResponse>(this.endPoint, this.connectionHandler, operationName, request, serializer, cancellationToken);
        }

    }
}
