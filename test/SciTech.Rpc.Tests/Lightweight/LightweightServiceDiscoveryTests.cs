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
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Lightweight
{
    [TestFixture]
    public class LightweightServiceDiscoveryTests
    {
        /// <summary>
        /// The services hash of an empty service list, the FNV-1a offset basis.
        /// </summary>
        private const string EmptyServicesHash = "cbf29ce484222325";

        private static readonly string SimpleServiceName = GetServiceName(typeof(ISimpleService));

        private static readonly string SimpleService2Name = GetServiceName(typeof(ISimpleService2));

        private ILoggerFactory loggerFactory;

        private IRpcSerializer serializer;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            this.loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            this.serializer = ServiceDiscoveryOperations.DiscoverySerializer;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            this.loggerFactory?.Dispose();
            this.loggerFactory = null;

            this.serializer = null;
        }

        [Test]
        public async Task FindServers_NoSingletons_ShouldReturn_ConnectionInfo()
        {
            var server = new LightweightRpcServer();
            var connectionInfo = new RpcConnectionInfo(new Uri("test://test"));
            var endPoint = new TestDiscoveryEndPoint(connectionInfo);
            server.AddEndPoint(endPoint);
            server.Start();
            try
            {
                var clientId = Guid.NewGuid();
                var response = await endPoint.Listeners.First().SendReceiveDatagramAsync<RpcFindServersRequest, RpcFindServersResponse>(
                    ServiceDiscoveryOperations.FindServers,
                    new RpcFindServersRequest(clientId, null),
                    this.serializer);

                Assert.NotNull(response);
                Assert.AreEqual(clientId, response.ClientId);
                Assert.AreEqual(endPoint.GetConnectionInfo(server.ServicePublisher.ServerId), response.ConnectionInfo);
                Assert.IsNull(response.Properties);
                Assert.IsNull(response.Matches);
                Assert.AreEqual(0, response.ServiceCount);
                Assert.AreEqual(EmptyServicesHash, response.ServicesHash);
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }

        [Test]
        public async Task FindServers_Should_Return_MatchesAndProperties()
        {
            var options = new RpcServerOptions { ServerProperties = { ["product"] = "Thermostat", ["version"] = "1.2.3" } };
            var server = new LightweightRpcServer(options: options);
            var endPoint = new TestDiscoveryEndPoint(new RpcConnectionInfo(new Uri("test://test")));
            server.AddEndPoint(endPoint);

            server.PublishSingleton<ISimpleService>();
            server.PublishSingleton<ISimpleService2>();

            server.Start();
            try
            {
                var response = await endPoint.Listeners.First().SendReceiveDatagramAsync<RpcFindServersRequest, RpcFindServersResponse>(
                    ServiceDiscoveryOperations.FindServers,
                    new RpcFindServersRequest(Guid.NewGuid(), new[] { SimpleService2Name, "Unknown.Service", SimpleServiceName }),
                    this.serializer);

                Assert.NotNull(response);
                CollectionAssert.AreEqual(new[] { 0, 2 }, response.Matches);
                Assert.AreEqual(2, response.ServiceCount);
                Assert.AreEqual(ComputeExpectedHash(SimpleServiceName, SimpleService2Name), response.ServicesHash);
                CollectionAssert.AreEquivalent(options.ServerProperties, response.Properties);
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }

        [Test]
        public async Task GetServices_Should_ReturnAllServicesInPages()
        {
            var server = new LightweightRpcServer();
            var endPoint = new TestDiscoveryEndPoint(new RpcConnectionInfo(new Uri("test://test")));
            server.AddEndPoint(endPoint);

            var expectedServices = PublishLongNameServices(server);

            server.Start();
            try
            {
                var listener = endPoint.Listeners.First();
                var clientId = Guid.NewGuid();
                var findResponse = await listener.SendReceiveDatagramAsync<RpcFindServersRequest, RpcFindServersResponse>(
                    ServiceDiscoveryOperations.FindServers, new RpcFindServersRequest(clientId, null), this.serializer);

                var services = new List<string>();
                int pageCount = 0;
                while (services.Count < findResponse.ServiceCount)
                {
                    var requestData = ServiceDiscoveryOperations.PadRequest(LightweightStubHelper.GetRequestData(
                        ServiceDiscoveryOperations.GetServices, 1, new RpcGetServicesRequest(clientId, server.ServerId.ToGuid(), services.Count), this.serializer));
                    var responseData = await listener.HandleDatagramAsync(requestData);

                    Assert.NotNull(responseData);
                    Assert.LessOrEqual(responseData.Length, ServiceDiscoveryOperations.MaxDatagramSize);

                    var response = LightweightStubHelper.GetResponseFromData<RpcGetServicesResponse>(this.serializer, responseData);
                    Assert.AreEqual(clientId, response.ClientId);
                    Assert.AreEqual(server.ServerId.ToGuid(), response.ServerId);
                    Assert.AreEqual(findResponse.ServicesHash, response.ServicesHash);
                    Assert.AreEqual(services.Count, response.Offset);
                    Assert.IsNotEmpty(response.Services);

                    services.AddRange(response.Services);
                    pageCount++;
                }

                Assert.Greater(pageCount, 1, "The services should not fit in a single page.");
                CollectionAssert.AreEqual(expectedServices, services);
                Assert.AreEqual(ComputeExpectedHash(expectedServices.ToArray()), findResponse.ServicesHash);

                // Beyond the end.
                var endResponse = await listener.SendReceiveDatagramAsync<RpcGetServicesRequest, RpcGetServicesResponse>(
                    ServiceDiscoveryOperations.GetServices, new RpcGetServicesRequest(clientId, server.ServerId.ToGuid(), services.Count), this.serializer);
                Assert.IsEmpty(endResponse.Services);

                // Requests for other servers are not answered, since they're multicast.
                var otherServerData = ServiceDiscoveryOperations.PadRequest(LightweightStubHelper.GetRequestData(
                    ServiceDiscoveryOperations.GetServices, 1, new RpcGetServicesRequest(clientId, Guid.NewGuid(), 0), this.serializer));
                Assert.IsNull(await listener.HandleDatagramAsync(otherServerData));
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }

        [Test]
        public async Task Datagram_ServiceOperation_ShouldBe_Ignored()
        {
            var server = new LightweightRpcServer();
            var endPoint = new TestDiscoveryEndPoint(new RpcConnectionInfo(new Uri("test://test")));
            server.AddEndPoint(endPoint);

            var serviceImpl = new CountingSimpleService();
            using var publishedService = server.PublishSingleton<ISimpleService>(serviceImpl);

            server.Start();
            try
            {
                // Datagrams are unauthenticated, so only discovery operations may be invoked through them.
                var requestData = ServiceDiscoveryOperations.PadRequest(LightweightStubHelper.GetRequestData(
                    "SciTech.Rpc.Tests.SimpleService.Add", 1, new RpcObjectRequest<int, int>(RpcObjectId.Empty, 5, 6), this.serializer));
                var responseData = await endPoint.Listeners.First().HandleDatagramAsync(requestData);

                Assert.IsNull(responseData);
                Assert.AreEqual(0, serviceImpl.CallCount);
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }

        [Test]
        public async Task Datagram_AcceptsOperationNumber_Should_EchoOperation()
        {
            var server = new LightweightRpcServer();
            var endPoint = new TestDiscoveryEndPoint(new RpcConnectionInfo(new Uri("test://test")));
            server.AddEndPoint(endPoint);

            server.Start();
            try
            {
                // The discovery client identifies responses by the operation name, so a datagram response must
                // always echo it, even if the request (incorrectly) accepts an operation number.
                byte[] requestData;
                using (var writer = new LightweightRpcFrameWriter(65536))
                {
                    var frame = new LightweightRpcFrame(RpcFrameType.UnaryRequest, 1,
                        ServiceDiscoveryOperations.FindServers, RpcOperationFlags.AcceptsOperationNumber, 0, null);
                    requestData = ServiceDiscoveryOperations.PadRequest(writer.WriteFrame(frame, new RpcFindServersRequest(Guid.NewGuid(), null), this.serializer));
                }

                var responseData = await endPoint.Listeners.First().HandleDatagramAsync(requestData);

                Assert.NotNull(responseData);
                Assert.AreEqual(RpcFrameState.Full, LightweightRpcFrame.TryRead(responseData, 65536, out var responseFrame));
                Assert.AreEqual(ServiceDiscoveryOperations.FindServers, responseFrame.RpcOperation);
                Assert.AreEqual(RpcOperationFlags.None, responseFrame.OperationFlags & RpcOperationFlags.HasOperationNumber);
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }

        [Test]
        public async Task Datagram_OutsideSizeLimits_ShouldBe_Ignored()
        {
            var server = new LightweightRpcServer();
            var endPoint = new TestDiscoveryEndPoint(new RpcConnectionInfo(new Uri("test://test")));
            server.AddEndPoint(endPoint);

            server.Start();
            try
            {
                var listener = endPoint.Listeners.First();
                var frameData = LightweightStubHelper.GetRequestData(
                    ServiceDiscoveryOperations.FindServers, 1, new RpcFindServersRequest(Guid.NewGuid(), null), this.serializer);
                Assert.Less(frameData.Length, ServiceDiscoveryOperations.MinRequestSize);

                // A request that isn't padded could be used to make the server send more data than it received.
                Assert.IsNull(await listener.HandleDatagramAsync(frameData));

                var paddedData = ServiceDiscoveryOperations.PadRequest(frameData);
                Assert.AreEqual(ServiceDiscoveryOperations.MinRequestSize, paddedData.Length);
                Assert.NotNull(await listener.HandleDatagramAsync(paddedData));

                var maxData = new byte[ServiceDiscoveryOperations.MaxDatagramSize];
                Buffer.BlockCopy(frameData, 0, maxData, 0, frameData.Length);
                Assert.NotNull(await listener.HandleDatagramAsync(maxData));

                var tooLongData = new byte[ServiceDiscoveryOperations.MaxDatagramSize + 1];
                Buffer.BlockCopy(frameData, 0, tooLongData, 0, frameData.Length);
                Assert.IsNull(await listener.HandleDatagramAsync(tooLongData));

                // Invalid data, e.g. an unknown frame version, is ignored, and the server keeps answering.
                var invalidData = (byte[])paddedData.Clone();
                invalidData[0] = 0xFF;
                Assert.IsNull(await listener.HandleDatagramAsync(invalidData));
                Assert.IsNull(await listener.HandleDatagramAsync(new byte[ServiceDiscoveryOperations.MinRequestSize]));
                Assert.NotNull(await listener.HandleDatagramAsync(paddedData));
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }

        [Test]
        public void ServerProperties_Should_BeLimited()
        {
            var maxProperties = Enumerable.Range(0, RpcServerProperties.MaxCount).ToDictionary(i => $"k{i}", i => new string('v', 30));
            Assert.AreEqual(RpcServerProperties.MaxTotalSize, maxProperties.Sum(p => p.Key.Length + p.Value.Length));
            RpcServerProperties.Validate(maxProperties);

            var tooManyProperties = Enumerable.Range(0, RpcServerProperties.MaxCount + 1).ToDictionary(i => $"k{i}", i => "v");
            Assert.Throws<ArgumentException>(() => RpcServerProperties.Validate(tooManyProperties));

            var tooLargeProperties = new Dictionary<string, string>(maxProperties) { ["k0"] = new string('v', 31) };
            Assert.Throws<ArgumentException>(() => RpcServerProperties.Validate(tooLargeProperties));

            // Sizes are in UTF-8 bytes.
            var nonAsciiProperties = new Dictionary<string, string>(maxProperties) { ["k0"] = new string('v', 29) + "ä" };
            Assert.Throws<ArgumentException>(() => RpcServerProperties.Validate(nonAsciiProperties));

            Assert.Throws<ArgumentException>(() => RpcServerProperties.Validate(new Dictionary<string, string> { [""] = "v" }));

            var options = new RpcServerOptions();
            foreach (var pair in tooManyProperties) options.ServerProperties.Add(pair.Key, pair.Value);
            Assert.Throws<ArgumentException>(() => new LightweightRpcServer(options: options));
        }

        [Test]
        public void DiscoveryEndPoint_Should_LimitConnectionInfo()
        {
            Assert.DoesNotThrow(() => new LightweightDiscoveryEndPoint(new RpcConnectionInfo(new string('a', 128), new Uri("lightweight.tcp://host:1234"))));
            Assert.Throws<ArgumentException>(() => new LightweightDiscoveryEndPoint(new RpcConnectionInfo(new string('a', 129), new Uri("lightweight.tcp://host:1234"))));
            Assert.Throws<ArgumentException>(() => new LightweightDiscoveryEndPoint(new RpcConnectionInfo("host", new Uri($"lightweight.tcp://host:1234/{new string('a', 250)}"))));
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
        public async Task Multicast_FindServers_ShouldReturn_Matches()
        {
            var server = new LightweightRpcServer();
            var endPoint = new LightweightDiscoveryEndPoint(new RpcConnectionInfo(new Uri("test://test")));
            server.AddEndPoint(endPoint);

            server.PublishSingleton<ISimpleService>();
            server.PublishSingleton<ISimpleService2>();

            server.Start();
            try
            {
                var responses = await this.MulticastFindServersAsync(new[] { SimpleServiceName, SimpleService2Name }, server.ServerId);

                CollectionAssert.AreEqual(new[] { 0, 1 }, responses[server.ServerId].Matches);
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }

        [Test]
        public async Task DualServer_Multicast_FindServers_ShouldBothRespond()
        {
            var server = new LightweightRpcServer();
            var server2 = new LightweightRpcServer();
            var endPoint = new LightweightDiscoveryEndPoint(new RpcConnectionInfo(new Uri("test://test")));
            server.AddEndPoint(endPoint);
            server2.AddEndPoint(endPoint);

            server.PublishSingleton<ISimpleService>();
            server2.PublishSingleton<ISimpleService2>();

            server.Start();
            server2.Start();
            try
            {
                // Both servers respond, also the one that doesn't publish the requested service.
                var responses = await this.MulticastFindServersAsync(new[] { SimpleServiceName }, server.ServerId, server2.ServerId);

                CollectionAssert.AreEqual(new[] { 0 }, responses[server.ServerId].Matches);
                Assert.IsNull(responses[server2.ServerId].Matches);
                Assert.AreEqual(1, responses[server2.ServerId].ServiceCount);
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

            var discoveryClient = this.CreateDiscoveryClient();

            // Other discovery servers may be running on the network, so only consider services from the test server.
            bool IsTestService(DiscoveredService service) => service.ConnectionInfo.ServerId == server.ServerId;

            var eventDiscoveredServices = new List<DiscoveredService>();
            bool serviceLost = false;
            discoveryClient.ServiceDiscovered += (s, e) => { if (IsTestService(e.DiscoveredService)) lock (eventDiscoveredServices) eventDiscoveredServices.Add(e.DiscoveredService); };
            discoveryClient.ServiceLost += (s, e) => { if (IsTestService(e.DiscoveredService)) serviceLost = true; };

            using var discoveryCts = new CancellationTokenSource();
            var discoveryTask = discoveryClient.FindServicesAsync(discoveryCts.Token);
            try
            {
                await Task.Delay(100);

                server.Start();
                try
                {
                    await WaitUntilAsync(() => discoveryClient.DiscoveredServices.Count(IsTestService) == 2);

                    Assert.IsFalse(serviceLost);
                    var discoveredServices = discoveryClient.DiscoveredServices.Where(IsTestService).ToList();
                    CollectionAssert.AreEquivalent(new[] { SimpleServiceName, SimpleService2Name }, discoveredServices.Select(s => s.Service));
                    await WaitUntilAsync(() => { lock (eventDiscoveredServices) return eventDiscoveredServices.Count == 2; });
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
            var endPoint2 = new TcpRpcEndPoint("localhost", "127.0.0.1", TestEndPoints.TcpPort2, false);
            var discoveryEndPoint2 = new LightweightDiscoveryEndPoint(endPoint2.GetConnectionInfo(server2.ServerId));
            server2.AddEndPoint(endPoint2);
            server2.AddEndPoint(discoveryEndPoint2);

            server.PublishSingleton<ISimpleService>();
            server2.PublishSingleton<ISimpleService2>();

            var discoveryClient = this.CreateDiscoveryClient();

            // Other discovery servers may be running on the network, so only consider services from the test servers.
            bool IsTestService(DiscoveredService service)
                => service.ConnectionInfo.ServerId == server.ServerId || service.ConnectionInfo.ServerId == server2.ServerId;

            using var discoveryCts = new CancellationTokenSource();
            var discoveryTask = discoveryClient.FindServicesAsync(discoveryCts.Token);
            await Task.Delay(100);

            server.Start();
            server2.Start();
            try
            {
                await WaitUntilAsync(() => discoveryClient.DiscoveredServices.Count(IsTestService) == 2);

                var discoveredServices = discoveryClient.DiscoveredServices.Where(IsTestService).ToList();
                Assert.AreEqual(2, discoveredServices.Count);

                discoveryCts.Cancel();
                await discoveryTask.DefaultTimeout();
            }
            finally
            {
                await server.ShutdownAsync();
                await server2.ShutdownAsync();
            }
        }

        [Test]
        public async Task DiscoveryClient_WithRequestedServices_Should_DiscoverIncompatibleServer()
        {
            var server = new LightweightRpcServer(options: new RpcServerOptions { ServerProperties = { ["version"] = "1" } });
            var endPoint = new TcpRpcEndPoint("localhost", "127.0.0.1", ClientServerTestsBase.TcpTestPort, false);
            server.AddEndPoint(endPoint);
            server.AddEndPoint(new LightweightDiscoveryEndPoint(endPoint.GetConnectionInfo(server.ServerId)));
            server.PublishSingleton<ISimpleService>();

            var server2 = new LightweightRpcServer(options: new RpcServerOptions { ServerProperties = { ["version"] = "2" } });
            var endPoint2 = new TcpRpcEndPoint("localhost", "127.0.0.1", TestEndPoints.TcpPort2, false);
            server2.AddEndPoint(endPoint2);
            server2.AddEndPoint(new LightweightDiscoveryEndPoint(endPoint2.GetConnectionInfo(server2.ServerId)));
            server2.PublishSingleton<ISimpleService2>();

            server.Start();
            server2.Start();
            try
            {
                var discoveryClient = this.CreateDiscoveryClient();
                using var discoveryCts = new CancellationTokenSource();
                var discoveryTask = discoveryClient.FindServicesAsync(new[] { SimpleServiceName }, discoveryCts.Token);
                try
                {
                    DiscoveredServer GetServer(RpcServerId serverId) => discoveryClient.DiscoveredServers.FirstOrDefault(s => s.ServerId == serverId);

                    await WaitUntilAsync(() => GetServer(server.ServerId) != null && GetServer(server2.ServerId) != null);

                    var discoveredServer = GetServer(server.ServerId);
                    CollectionAssert.AreEqual(new[] { SimpleServiceName }, discoveredServer.Services);
                    Assert.AreEqual("1", discoveredServer.Properties["version"]);
                    Assert.AreEqual(1, discoveredServer.ServiceCount);

                    // The server without the requested service is discovered, so that it can be shown as incompatible.
                    var discoveredServer2 = GetServer(server2.ServerId);
                    Assert.IsEmpty(discoveredServer2.Services);
                    Assert.AreEqual("2", discoveredServer2.Properties["version"]);
                    Assert.AreEqual(1, discoveredServer2.ServiceCount);

                    Assert.IsFalse(discoveryClient.DiscoveredServices.Any(s => s.ConnectionInfo.ServerId == server2.ServerId));
                }
                finally
                {
                    discoveryCts.Cancel();
                    await discoveryTask.DefaultTimeout();
                }
            }
            finally
            {
                await server.ShutdownAsync();
                await server2.ShutdownAsync();
            }
        }

        [Test]
        public async Task DiscoveryClient_Should_RetrieveAllServicesInPages()
        {
            var server = new LightweightRpcServer();
            var endPoint = new TcpRpcEndPoint("localhost", "127.0.0.1", ClientServerTestsBase.TcpTestPort, false);
            server.AddEndPoint(endPoint);
            server.AddEndPoint(new LightweightDiscoveryEndPoint(endPoint.GetConnectionInfo(server.ServerId)));

            var expectedServices = PublishLongNameServices(server);

            server.Start();
            try
            {
                var discoveryClient = this.CreateDiscoveryClient();
                using var discoveryCts = new CancellationTokenSource();
                var discoveryTask = discoveryClient.FindServicesAsync(discoveryCts.Token);
                try
                {
                    DiscoveredServer GetServer() => discoveryClient.DiscoveredServers.FirstOrDefault(s => s.ServerId == server.ServerId);

                    await WaitUntilAsync(() => GetServer()?.Services.Count == expectedServices.Count);

                    CollectionAssert.AreEqual(expectedServices, GetServer().Services);
                    Assert.AreEqual(expectedServices.Count, GetServer().ServiceCount);
                }
                finally
                {
                    discoveryCts.Cancel();
                    await discoveryTask.DefaultTimeout();
                }
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }

        [Test]
        public async Task DiscoveryClient_Should_LoseStoppedServer()
        {
            var server = new LightweightRpcServer();
            var endPoint = new TcpRpcEndPoint("localhost", "127.0.0.1", ClientServerTestsBase.TcpTestPort, false);
            server.AddEndPoint(endPoint);
            server.AddEndPoint(new LightweightDiscoveryEndPoint(endPoint.GetConnectionInfo(server.ServerId)));
            server.PublishSingleton<ISimpleService>();

            var discoveryClient = this.CreateDiscoveryClient();
            discoveryClient.RequestInterval = TimeSpan.FromMilliseconds(200);
            discoveryClient.ServerLostTimeout = TimeSpan.FromSeconds(1);

            bool serverLost = false;
            discoveryClient.ServerLost += (s, e) => { if (e.DiscoveredServer.ServerId == server.ServerId) serverLost = true; };

            server.Start();
            bool isServerStopped = false;
            try
            {
                using var discoveryCts = new CancellationTokenSource();
                var discoveryTask = discoveryClient.FindServicesAsync(new[] { SimpleServiceName }, discoveryCts.Token);
                try
                {
                    await WaitUntilAsync(() => discoveryClient.DiscoveredServers.Any(s => s.ServerId == server.ServerId));

                    // Several requests without a lost server.
                    await Task.Delay(600);
                    Assert.IsFalse(serverLost);

                    await server.ShutdownAsync();
                    isServerStopped = true;

                    await WaitUntilAsync(() => serverLost);
                    Assert.IsFalse(discoveryClient.DiscoveredServers.Any(s => s.ServerId == server.ServerId));
                }
                finally
                {
                    discoveryCts.Cancel();
                    await discoveryTask.DefaultTimeout();
                }
            }
            finally
            {
                if (!isServerStopped)
                {
                    await server.ShutdownAsync();
                }
            }
        }

        [Test]
        public void DiscoveryClient_TooManyRequestedServices_Should_Throw()
        {
            var discoveryClient = this.CreateDiscoveryClient();
            var services = Enumerable.Range(0, ServiceDiscoveryOperations.MaxRequestedServices + 1).Select(i => $"Service{i}");

            Assert.ThrowsAsync<ArgumentException>(() => discoveryClient.FindServicesAsync(services, CancellationToken.None));
            Assert.ThrowsAsync<ArgumentException>(() => discoveryClient.FindServicesAsync(new[] { new string('a', ServiceDiscoveryOperations.MaxDatagramSize) }, CancellationToken.None));
        }

        [TestCase(AddressFamily.InterNetwork)]
        [TestCase(AddressFamily.InterNetworkV6)]
        public async Task DiscoveryClient_Should_DiscoverOnAddressFamily(AddressFamily addressFamily)
        {
            var addressFamilies = new[] { addressFamily };
            if (!LightweightDiscoveryEndPoint.GetDiscoveryInterfaces(addressFamilies).Any())
            {
                Assert.Ignore($"No multicast capable network interface with an {addressFamily} address.");
            }

            var server = new LightweightRpcServer();
            var endPoint = new TcpRpcEndPoint("localhost", "127.0.0.1", ClientServerTestsBase.TcpTestPort, false);
            var discoveryEndPoint = new LightweightDiscoveryEndPoint(endPoint.GetConnectionInfo(server.ServerId))
            {
                AddressFamilies = addressFamilies
            };
            server.AddEndPoint(endPoint);
            server.AddEndPoint(discoveryEndPoint);

            server.PublishSingleton<ISimpleService>();

            var discoveryClient = this.CreateDiscoveryClient();
            discoveryClient.AddressFamilies = addressFamilies;

            server.Start();
            try
            {
                using var discoveryCts = new CancellationTokenSource();
                var discoveryTask = discoveryClient.FindServicesAsync(discoveryCts.Token);
                try
                {
                    await WaitUntilAsync(() => discoveryClient.DiscoveredServices.Any(s => s.ConnectionInfo.ServerId == server.ServerId));

                    Assert.IsFalse(discoveryTask.IsCompleted, "Discovery should run until cancelled.");
                    var discoveredService = discoveryClient.DiscoveredServices.SingleOrDefault(s => s.ConnectionInfo.ServerId == server.ServerId);
                    Assert.NotNull(discoveredService, $"Server not discovered on {addressFamily}.");
                    Assert.AreEqual(WellKnownRpcSchemes.LightweightTcp, discoveredService.ConnectionInfo.HostUrl.Scheme);
                }
                finally
                {
                    discoveryCts.Cancel();
                    await discoveryTask.DefaultTimeout();
                }
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }

        /// <summary>
        /// Computes the services hash as specified, independently of the implementation.
        /// </summary>
        private static string ComputeExpectedHash(params string[] services)
        {
            ulong hash = 14695981039346656037;
            foreach (var name in services.Select(Encoding.UTF8.GetBytes).OrderBy(n => n, Utf8Comparer.Instance))
            {
                foreach (var b in name.Concat(new byte[] { 0x0A }))
                {
                    hash ^= b;
                    hash = unchecked(hash * 1099511628211);
                }
            }

            return hash.ToString("x16");
        }

        private static string GetServiceName(Type serviceType) => RpcBuilderUtil.TryGetServiceInfoFromType(serviceType).FullName;

        /// <summary>
        /// Publishes services with long names, so that they don't fit in a single datagram, plus a service whose name
        /// is too long to be discovered.
        /// </summary>
        /// <returns>The names of the discoverable services, in discovery order.</returns>
        private static List<string> PublishLongNameServices(LightweightRpcServer server)
        {
            server.PublishSingleton<ILongNameService1>();
            server.PublishSingleton<ILongNameService2>();
            server.PublishSingleton<ILongNameService3>();
            server.PublishSingleton<ILongNameService4>();
            server.PublishSingleton<ILongNameService5>();
            server.PublishSingleton<ILongNameService6>();
            server.PublishSingleton<ITooLongNameService>();
            server.PublishSingleton<ISimpleService>();

            Assert.Greater(Encoding.UTF8.GetByteCount(GetServiceName(typeof(ITooLongNameService))), ServiceDiscoveryOperations.MaxServiceNameSize);

            return new[]
            {
                typeof(ILongNameService1), typeof(ILongNameService2), typeof(ILongNameService3),
                typeof(ILongNameService4), typeof(ILongNameService5), typeof(ILongNameService6), typeof(ISimpleService)
            }
            .Select(GetServiceName)
            .OrderBy(Encoding.UTF8.GetBytes, Utf8Comparer.Instance)
            .ToList();
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            // Discovery requests are sent every 2 seconds by default.
            var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (!condition())
            {
                if (DateTime.UtcNow > timeout)
                {
                    Assert.Fail("Timeout waiting for discovery.");
                }

                await Task.Delay(50);
            }
        }

        private LightweightDiscoveryClient CreateDiscoveryClient()
            => new LightweightDiscoveryClient(this.loggerFactory.CreateLogger<LightweightDiscoveryClient>());

        /// <summary>
        /// Multicasts a FindServers request and waits for a response from each of the specified servers. Responses
        /// from other discovery servers on the network, and duplicate responses (e.g. received on several interfaces),
        /// are ignored.
        /// </summary>
        private async Task<Dictionary<RpcServerId, RpcFindServersResponse>> MulticastFindServersAsync(string[] services, params RpcServerId[] serverIds)
        {
            using var client = new UdpClient(AddressFamily.InterNetwork);
            client.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

            var clientId = Guid.NewGuid();
            byte[] requestData = ServiceDiscoveryOperations.PadRequest(LightweightStubHelper.GetRequestData(
                ServiceDiscoveryOperations.FindServers, 1, new RpcFindServersRequest(clientId, services), this.serializer));

            // Send the request on each interface the discovery servers listen on. Using only the default
            // multicast interface fails if, e.g., a VPN adapter is preferred.
            var discoveryEp = new IPEndPoint(LightweightDiscoveryEndPoint.DefaultMulticastAddress, LightweightDiscoveryEndPoint.DefaultDiscoveryPort);
            foreach (var interfaceAddress in GetMulticastInterfaceAddresses())
            {
                client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, interfaceAddress.GetAddressBytes());
                var bytesSent = await client.SendAsync(requestData, requestData.Length, discoveryEp);
                Assert.AreEqual(requestData.Length, bytesSent);
            }

            var responses = new Dictionary<RpcServerId, RpcFindServersResponse>();
            while (responses.Count < serverIds.Length)
            {
                var receiveRes = await client.ReceiveAsync().DefaultTimeout();
                Assert.LessOrEqual(receiveRes.Buffer.Length, ServiceDiscoveryOperations.MaxDatagramSize);

                var response = LightweightStubHelper.GetResponseFromData<RpcFindServersResponse>(this.serializer, receiveRes.Buffer);
                var serverId = response?.ConnectionInfo?.ServerId;
                if (response?.ClientId == clientId && serverId is RpcServerId id && Array.IndexOf(serverIds, id) >= 0 && !responses.ContainsKey(id))
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
            return LightweightDiscoveryEndPoint.GetDiscoveryInterfaces(new[] { AddressFamily.InterNetwork })
                .Select(i => i.Address);
        }

        private sealed class Utf8Comparer : IComparer<byte[]>
        {
            internal static readonly Utf8Comparer Instance = new Utf8Comparer();

            public int Compare(byte[] x, byte[] y)
            {
                for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
                {
                    if (x[i] != y[i]) return x[i].CompareTo(y[i]);
                }

                return x.Length.CompareTo(y.Length);
            }
        }
    }

    internal static class LongServiceNames
    {
        /// <summary>
        /// 200 characters, so that a few service names fill a discovery datagram.
        /// </summary>
        internal const string Suffix =
            "_0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789"
            + "_012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678";
    }

    [RpcService(Namespace = "SciTech.Rpc.Tests.Discovery", Name = "LongNameService1" + LongServiceNames.Suffix)]
    public interface ILongNameService1 { Task<int> GetValueAsync(); }

    [RpcService(Namespace = "SciTech.Rpc.Tests.Discovery", Name = "LongNameService2" + LongServiceNames.Suffix)]
    public interface ILongNameService2 { Task<int> GetValueAsync(); }

    [RpcService(Namespace = "SciTech.Rpc.Tests.Discovery", Name = "LongNameService3" + LongServiceNames.Suffix)]
    public interface ILongNameService3 { Task<int> GetValueAsync(); }

    [RpcService(Namespace = "SciTech.Rpc.Tests.Discovery", Name = "LongNameService4" + LongServiceNames.Suffix)]
    public interface ILongNameService4 { Task<int> GetValueAsync(); }

    [RpcService(Namespace = "SciTech.Rpc.Tests.Discovery", Name = "LongNameService5" + LongServiceNames.Suffix)]
    public interface ILongNameService5 { Task<int> GetValueAsync(); }

    [RpcService(Namespace = "SciTech.Rpc.Tests.Discovery", Name = "LongNameService6" + LongServiceNames.Suffix)]
    public interface ILongNameService6 { Task<int> GetValueAsync(); }

    [RpcService(Namespace = "SciTech.Rpc.Tests.Discovery", Name = "TooLongNameService" + LongServiceNames.Suffix + LongServiceNames.Suffix)]
    public interface ITooLongNameService { Task<int> GetValueAsync(); }

    internal class CountingSimpleService : ISimpleService
    {
        private int callCount;

        internal int CallCount => this.callCount;

        public Task<int> AddAsync(int a, int b) { this.Called(); return Task.FromResult(a + b); }

        public Task<int[]> GetArrayAsync(int count) { this.Called(); return Task.FromResult(new int[count]); }

        public Task<double> GetValueAsync() { this.Called(); return Task.FromResult(0.0); }

        public Task SetValueAsync(double value) { this.Called(); return Task.CompletedTask; }

        public Task<int> SumAsync(int[] data) { this.Called(); return Task.FromResult(data.Sum()); }

        private void Called() => Interlocked.Increment(ref this.callCount);
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

        internal ValueTask<byte[]> HandleDatagramAsync(byte[] requestData)
        {
            Assert.IsTrue(this.IsListening);

            return this.connectionHandler.HandleDatagramAsync(this.endPoint, requestData, CancellationToken.None);
        }

    }
}
