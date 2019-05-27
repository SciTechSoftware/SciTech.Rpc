using NUnit.Framework;
using SciTech.Rpc.Client;
using SciTech.Rpc.Grpc.Client;
using SciTech.Rpc.Grpc.Server;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using SciTech.Rpc.Tests;
using SciTech.Threading;
using System;
using System.Threading.Tasks;

namespace SciTech.Rpc.Grpc.Tests
{
    /// <summary>
    /// These are old initial tests. Should probably be moved/merged with GrpcClientServerTests
    /// </summary>
    [TestFixtureSource(nameof(FixtureArgs))]
    public class GrpcFullStackTests : GrpcCoreFullStackTestsBase
    {
        private static object[] FixtureArgs = {
            new object[] { new ProtobufSerializer() },
            new object[] { new DataContractGrpcSerializer(null) }
        };

        private RpcServiceOptions options;

        public GrpcFullStackTests(IRpcSerializer serializer)
        {
            this.options = new RpcServiceOptions { Serializer = serializer };
        }

        [Test]
        public async Task BlockingServiceCallTest()
        {
            var serverBuilder = new RpcServiceDefinitionBuilder();
            serverBuilder
                .RegisterService<IBlockingService>()
                .RegisterService<ISimpleService>();

            var host = new GrpcServer(serverBuilder, null, this.options);

            host.AddEndPoint(CreateEndPoint());

            host.Start();

            try
            {
                var serviceImpl = new TestBlockingSimpleServiceImpl();
                using (var publishScope = host.PublishServiceInstance(serviceImpl))
                {
                    var objectId = publishScope.Value.ObjectId;

                    var proxyGenerator = new GrpcProxyProvider();
                    var connection = this.CreateGrpcConnection(proxyGenerator);

                    var clientService = connection.GetServiceInstance<IBlockingServiceClient>(objectId);

                    int blockingRes = clientService.Add(12, 13);
                    Assert.AreEqual(12 + 13, blockingRes);

                    int asyncRes = await clientService.AddAsync(8, 9);
                    Assert.AreEqual(8 + 9, asyncRes);

                    clientService.Value = 123.45;
                    Assert.AreEqual(123.45, await clientService.GetValueAsync());

                    await clientService.SetValueAsync(543.21);
                    Assert.AreEqual(543.21, clientService.Value);
                }
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }

        [TearDown]
        public new void Cleanup()
        {
            RpcStubOptions.TestDelayEventHandlers = false;
        }

        [Test]
        public async Task DeviceServiceTest()
        {
            var serverBuilder = new RpcServiceDefinitionBuilder();
            serverBuilder.RegisterService<IThermostatService>();
            var host = new GrpcServer(serverBuilder, null,  this.options);

            host.AddEndPoint(CreateEndPoint());

            host.Start();
            try
            {
                var serviceImpl = new ThermostatServiceImpl();
                using (var publishScope = host.PublishServiceInstance(serviceImpl))
                {
                    var objectId = publishScope.Value.ObjectId;
                    var proxyGenerator = new GrpcProxyProvider();
                    GrpcServerConnection connection = this.CreateGrpcConnection(proxyGenerator);

                    var clientService = connection.GetServiceInstance<IThermostatServiceClient>(objectId);
                    var acoId = clientService.DeviceAcoId;

                    var baseClientService = (IDeviceServiceClient)clientService;
                    var acoId2 = baseClientService.DeviceAcoId;
                    Assert.AreEqual(acoId, acoId2);
                }
            }
            finally
            {
                await host.ShutdownAsync();
            }

        }

        [Test]
        public async Task EventHandlersTest()
        {
            var serverBuilder = new RpcServiceDefinitionBuilder();
            serverBuilder.RegisterService<ISimpleServiceWithEvents>();

            var host = new GrpcServer(serverBuilder, null, this.options);
            host.AddEndPoint(CreateEndPoint());
            host.Start();

            try
            {
                var serviceImpl = new TestServiceWithEventsImpl();
                using (var publishScope = host.PublishServiceInstance(serviceImpl))
                {
                    var objectId = publishScope.Value.ObjectId;

                    var proxyGenerator = new GrpcProxyProvider();
                    var connection = this.CreateGrpcConnection(proxyGenerator);

                    var clientService = connection.GetServiceInstance<ISimpleServiceWithEvents>(objectId);

                    TaskCompletionSource<ValueChangedEventArgs> detailedTcs = new TaskCompletionSource<ValueChangedEventArgs>();
                    EventHandler<ValueChangedEventArgs> detailedHandler = (s, e) =>
                    {
                        detailedTcs.SetResult(e);
                    };

                    clientService.DetailedValueChanged += detailedHandler;

                    await ((IRpcService)clientService).WaitForPendingEventHandlers();

                    clientService.SetValueAsync(12).Forget();

                    var completedTask = await Task.WhenAny(detailedTcs.Task, Task.Delay(1000));
                    Assert.AreEqual(detailedTcs.Task, completedTask);
                    Assert.IsTrue(completedTask.IsCompletedSuccessfully());

                    var detailedArgs = detailedTcs.Task.Result;

                    clientService.DetailedValueChanged -= detailedHandler;
                    await ((IRpcService)clientService).WaitForPendingEventHandlers();
                    clientService.SetValueAsync(13).Forget();

                    await Task.Delay(200);

                    Assert.IsFalse(serviceImpl.HasDetailedValueChangedHandler);
                    Assert.IsFalse(serviceImpl.HasValueChangedHandler);

                    Assert.AreEqual(12, detailedArgs.NewValue);
                    Assert.AreEqual(0, detailedArgs.OldValue);
                }
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }

        [SetUp]
        public void Init()
        {
            RpcStubOptions.TestDelayEventHandlers = true;
        }

        [Test]
        public async Task ReverseDeviceServiceTest()
        {
            var serverBuilder = new RpcServiceDefinitionBuilder();
            serverBuilder.RegisterService<IThermostatService>();
            var host = new GrpcServer(serverBuilder, null, this.options);

            host.AddEndPoint(CreateEndPoint());

            host.Start();
            try
            {
                var serviceImpl = new ThermostatServiceImpl();
                using (var publishScope = host.PublishServiceInstance(serviceImpl))
                {
                    var objectId = publishScope.Value.ObjectId;
                    var proxyGenerator = new GrpcProxyProvider();
                    var connection = this.CreateGrpcConnection(proxyGenerator);

                    var clientService = connection.GetServiceInstance<IDeviceServiceClient>(objectId);
                    var acoId = clientService.DeviceAcoId;

                    var baseClientService = connection.GetServiceInstance<IThermostatServiceClient>(objectId);
                    var acoId2 = baseClientService.DeviceAcoId;
                    Assert.AreEqual(acoId, acoId2);
                }
            }
            finally
            {
                await host.ShutdownAsync();
            }

        }

        [Test]
        public async Task ServiceProviderServiceCallTest()
        {
            var serverBuilder = new RpcServiceDefinitionBuilder();
            serverBuilder.RegisterSingletonService<IServiceProviderService>()
                .RegisterService<ISimpleService>();

            var host = new GrpcServer(serverBuilder, null, this.options);
            host.AddEndPoint(CreateEndPoint());

            host.Start();
            try
            {
                var serviceImpl = new ServiceProviderServiceImpl(host.ServicePublisher);

                using (var publishScope = host.PublishSingleton<IServiceProviderService>(serviceImpl))
                {
                    var proxyGenerator = new GrpcProxyProvider();
                    var connection = this.CreateGrpcConnection(proxyGenerator);

                    var clientService = connection.GetServiceSingleton<IServiceProviderServiceClient>();
                    var serviceRef = await clientService.GetSimpleServiceAsync();

                    var simpleService = connection.GetServiceInstance(serviceRef);

                    int res = await simpleService.AddAsync(8, 9);

                    Assert.AreEqual(8 + 9, res);
                }
            }
            finally
            {
                await host.ShutdownAsync();
            }

        }

        [Test]
        public async Task SimpleObjectServiceCallTest()
        {
            var serverBuilder = new RpcServiceDefinitionBuilder();
            serverBuilder.RegisterService<ISimpleService>();
            var host = new GrpcServer(serverBuilder, null, this.options);
            host.AddEndPoint(CreateEndPoint());

            host.Start();
            try
            {
                var serviceImpl = new TestSimpleServiceImpl();
                using (var publishScope = host.PublishServiceInstance(serviceImpl))
                {
                    var objectId = publishScope.Value.ObjectId;
                    var proxyGenerator = new GrpcProxyProvider();
                    var connection = this.CreateGrpcConnection(proxyGenerator);

                    var clientService = connection.GetServiceInstance<ISimpleService>(objectId);
                    int res = await clientService.AddAsync(8, 9);

                    Assert.AreEqual(8 + 9, res);
                }
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }

        private GrpcServerConnection CreateGrpcConnection(GrpcProxyProvider proxyGenerator)
        {
            return new GrpcServerConnection(CreateConnectionInfo(), TestCertificates.SslCredentials, proxyGenerator, this.options.Serializer);
        }
    }
}
