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
            var connectionInfo = new RpcServerConnectionInfo(new Uri("test://test"));
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
            var connectionInfo = new RpcServerConnectionInfo(new Uri("test://test"));
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
            var connectionInfo = new RpcServerConnectionInfo(new Uri("test://test"));
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
            var connectionInfo = new RpcServerConnectionInfo(new Uri("test://test"));
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
            var connectionInfo = new RpcServerConnectionInfo(new Uri("test://test"));
            var endPoint = new LightweightDiscoveryEndPoint(connectionInfo);
            server.AddEndPoint(endPoint);

            server.PublishSingleton<ISimpleService>();
            server.PublishSingleton<ISimpleService2>();

            server.Start();
            try
            {
                using var client = new UdpClient(AddressFamily.InterNetwork);
                client.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
                client.JoinMulticastGroup(LightweightDiscoveryEndPoint.DefaultMulticastAddress);

                // Receive early
                var receiveTask = client.ReceiveAsync();

                // Build request
                byte[] requestData = LightweightStubHelper.GetRequestData(ServiceDiscoveryOperations.GetPublishedSingletons, 0, new RpcRequest(), this.serializer);

                // Send it
                var discoveryEp = new System.Net.IPEndPoint(LightweightDiscoveryEndPoint.DefaultMulticastAddress, LightweightDiscoveryEndPoint.DefaultDiscoveryPort);
                var bytesSent = await client.SendAsync(requestData, requestData.Length, discoveryEp);
                Assert.AreEqual(requestData.Length, bytesSent);

                // Wait for response
                var receiveRes = await receiveTask.DefaultTimeout();

                var response = LightweightStubHelper.GetResponseFromData<RpcPublishedSingletonsResponse>(serializer, receiveRes.Buffer);
                Assert.NotNull(response);

                // And check it
                Assert.NotNull(response);
                Assert.AreEqual(2, response.Services.Length);
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
            var connectionInfo = new RpcServerConnectionInfo(new Uri("test://test"));
            var endPoint = new LightweightDiscoveryEndPoint(connectionInfo);
            server.AddEndPoint(endPoint);
            server2.AddEndPoint(endPoint);

            server.PublishSingleton<ISimpleService>();
            server2.PublishSingleton<ISimpleService2>();

            server.Start();
            server2.Start();
            try
            {
                using var client = new UdpClient(AddressFamily.InterNetwork);
                client.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
                client.JoinMulticastGroup(LightweightDiscoveryEndPoint.DefaultMulticastAddress);

                // Receive early
                var receiveTask = client.ReceiveAsync();

                // Build request
                byte[] requestData = LightweightStubHelper.GetRequestData(ServiceDiscoveryOperations.GetPublishedSingletons, 0, new RpcRequest(), this.serializer);

                // Send it
                var discoveryEp = new IPEndPoint(LightweightDiscoveryEndPoint.DefaultMulticastAddress, LightweightDiscoveryEndPoint.DefaultDiscoveryPort);
                var bytesSent = await client.SendAsync(requestData, requestData.Length, discoveryEp);
                Assert.AreEqual(requestData.Length, bytesSent);

                // Wait for response
                var receiveRes = await receiveTask.DefaultTimeout();
                var receiveRes2 = await client.ReceiveAsync().DefaultTimeout();

                var response = LightweightStubHelper.GetResponseFromData<RpcPublishedSingletonsResponse>(serializer, receiveRes.Buffer);
                Assert.NotNull(response);
                var response2 = LightweightStubHelper.GetResponseFromData<RpcPublishedSingletonsResponse>(serializer, receiveRes2.Buffer);
                Assert.NotNull(response2);

                // And check it
                Assert.NotNull(response);
                Assert.NotNull(response2);

                RpcPublishedSingletonsResponse server1Response;
                RpcPublishedSingletonsResponse server2Response;
                if (response.ConnectionInfo.ServerId == server.ServerId)
                {
                    server1Response = response;
                    server2Response = response2;
                } else
                {
                    server1Response = response2;
                    server2Response = response;
                }

                Assert.AreEqual(server.ServerId, server1Response.ConnectionInfo.ServerId);
                Assert.AreEqual(1, server1Response.Services.Length);

                Assert.AreEqual(server2.ServerId, server2Response.ConnectionInfo.ServerId);
                Assert.AreEqual(1, server2Response.Services.Length);
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

            var eventDiscoveredServices = new List<DiscoveredService>();
            bool serviceLost = false;
            discoveryClient.ServiceDiscovered += (s, e) => eventDiscoveredServices.Add(e.DiscoveredService);
            discoveryClient.ServiceLost += (s, e) => serviceLost = true;

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
                    var discoveredServices = discoveryClient.DiscoveredServices;
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

            var eventDiscoveredServices = new List<DiscoveredService>();
            bool serviceLost = false;
            discoveryClient.ServiceDiscovered += (s, e) => eventDiscoveredServices.Add(e.DiscoveredService);
            discoveryClient.ServiceLost += (s, e) => serviceLost = true;

            using var discoveryCts = new CancellationTokenSource();
            var discoveryTask = discoveryClient.FindServicesAsync(discoveryCts.Token);
            await Task.Delay(100);

            server.Start();
            server2.Start();
            try
            {
                await Task.Delay(2000);

                Assert.IsFalse(serviceLost);
                var discoveredServices = discoveryClient.DiscoveredServices;
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
    }

    internal class TestDiscoveryEndPoint : LightweightRpcEndPoint
    {
        RpcServerConnectionInfo connectionInfo;

        List<TestDiscoveryListener> listeners = new List<TestDiscoveryListener>();

        public TestDiscoveryEndPoint(RpcServerConnectionInfo connectionInfo)
        {
            this.connectionInfo = connectionInfo;
        }

        public IReadOnlyList<TestDiscoveryListener> Listeners => this.listeners;

        public override string DisplayName => connectionInfo.DisplayName;

        public override string HostName => connectionInfo.HostUrl.Host;

        public override RpcServerConnectionInfo GetConnectionInfo(RpcServerId serverId)
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

        public void Dispose()
        {
            if (this.IsListening) this.DisposedWhileListening = true;

            this.IsDisposed = true;
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
