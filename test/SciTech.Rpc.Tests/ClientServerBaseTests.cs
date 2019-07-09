using Moq;
using NUnit.Framework;
using SciTech.Rpc.Client;
using SciTech.Rpc.Server;
using SciTech.Threading;
using System;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests
{
    public abstract class ClientServerTests : ClientServerTestsBase
    {
        protected ClientServerTests(IRpcSerializer serializer, RpcConnectionType connectionType) :
            base(serializer, connectionType)
        {
        }

        [Test]
        public async Task AddRemoveMixedEventHandlersTest()
        {
            var serverBuilder = new RpcServiceDefinitionBuilder();
            serverBuilder.RegisterService<ISimpleServiceWithEvents>();

            var (host, connection) = this.CreateServerAndConnection(serverBuilder);
            var servicePublisher = host.ServicePublisher;
            host.Start();

            try
            {
                var serviceImpl = new TestServiceWithEventsImpl();
                using (var publishScope = servicePublisher.PublishInstance(serviceImpl))
                {
                    var objectId = publishScope.Value.ObjectId;
                    
                    var clientService = connection.GetServiceInstance<ISimpleServiceWithEvents>(objectId);

                    ValueChangedEventArgs detailedArgs = await TestMixedEventHandlers(clientService).DefaultTimeout();

                    Assert.IsFalse(serviceImpl.HasDetailedValueChangedHandler);
                    Assert.IsFalse(serviceImpl.HasValueChangedHandler);

                    detailedArgs = await TestMixedEventHandlers(clientService);

                    Assert.IsFalse(serviceImpl.HasDetailedValueChangedHandler);
                    Assert.IsFalse(serviceImpl.HasValueChangedHandler);
                }
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }

        [Test]
        public async Task BlockingServiceCallTest()
        {
            var serviceRegistrator = new RpcServiceDefinitionBuilder();
            serviceRegistrator
                .RegisterService<IBlockingService>()
                .RegisterService<ISimpleService>();

            var (host, connection) = this.CreateServerAndConnection(serviceRegistrator);
            var servicePublisher = host.ServicePublisher;
            //RpcServerId rpcServerId = servicePublisher.HostId;
            host.Start();

            try
            {
                var serviceImpl = new TestBlockingSimpleServiceImpl();
                using (var publishScope = servicePublisher.PublishInstance(serviceImpl))
                {
                    var objectId = publishScope.Value.ObjectId;

                    var clientService = connection.GetServiceInstance<IBlockingServiceClient>(objectId);

                    int blockingRes = clientService.Add(12, 13);
                    Assert.AreEqual(12 + 13, blockingRes);

                    int asyncRes = await clientService.AddAsync(8, 9);
                    Assert.AreEqual(8 + 9, asyncRes);

                    clientService.Value = 123.45;
                    Assert.AreEqual(123.45, await clientService.GetValueAsync());

                    await clientService.SetValueAsync(543.21).ConfigureAwait(false);
                    Assert.AreEqual(543.21, clientService.Value);

                    await connection.ShutdownAsync();
                }
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }


        [Test]
        public async Task DirectServiceProviderServiceCallTest()
        {
            var serverBuilder = new RpcServiceDefinitionBuilder();
            serverBuilder.RegisterService<IServiceProviderService>()
                .RegisterService<ISimpleService>();

            var (host, connection) = this.CreateServerAndConnection(serverBuilder);
            var servicePublisher = host.ServicePublisher;
            var rpcServerId = servicePublisher.ServerId;

            host.Start();

            try
            {
                var serviceImpl = new ServiceProviderServiceImpl(host.ServicePublisher);

                using (var publishScope = servicePublisher.PublishSingleton<IServiceProviderService>(serviceImpl))
                {
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
        public async Task GenericEventHandlerTest()
        {
            var serverBuilder = new RpcServiceDefinitionBuilder();
            serverBuilder.RegisterService<ISimpleServiceWithEvents>();

            var (host, connection) = this.CreateServerAndConnection(serverBuilder);
            var servicePublisher = host.ServicePublisher;
            host.Start();

            try
            {
                var serviceImpl1 = new TestServiceWithEventsImpl();
                var serviceImpl2 = new TestServiceWithEventsImpl();
                using (var publishScope1 = servicePublisher.PublishInstance(serviceImpl1))
                using (var publishScope2 = servicePublisher.PublishInstance(serviceImpl2))
                {
                    var objectId1 = publishScope1.Value.ObjectId;
                    var objectId2 = publishScope2.Value.ObjectId;
                    var clientService1 = connection.GetServiceInstance<ISimpleServiceWithEvents>(objectId1);
                    var clientService2 = connection.GetServiceInstance<ISimpleServiceWithEvents>(objectId2);

                    var detailedTcs1 = new TaskCompletionSource<ValueChangedEventArgs>();
                    EventHandler<ValueChangedEventArgs> detailedHandler1 = (s, e) =>
                    {
                        detailedTcs1.SetResult(e);
                    };
                    clientService1.DetailedValueChanged += detailedHandler1;

                    var detailedTcs2 = new TaskCompletionSource<ValueChangedEventArgs>();
                    EventHandler<ValueChangedEventArgs> detailedHandler2 = (s, e) =>
                    {
                        detailedTcs2.SetResult(e);
                    };
                    clientService2.DetailedValueChanged += detailedHandler2;


                    await ((IRpcService)clientService1).WaitForPendingEventHandlers().DefaultTimeout();
                    await ((IRpcService)clientService2).WaitForPendingEventHandlers().DefaultTimeout();

                    clientService1.SetValueAsync(12).Forget();
                    clientService2.SetValueAsync(24).Forget();

                    var detailedArgs1 = await detailedTcs1.Task.DefaultTimeout();
                    var detailedArgs2 = await detailedTcs2.Task.DefaultTimeout();

                    clientService1.DetailedValueChanged -= detailedHandler1;
                    await ((IRpcService)clientService1).WaitForPendingEventHandlers().DefaultTimeout();
                    await clientService1.SetValueAsync(13).DefaultTimeout();

                    // Verify 1
                    Assert.AreEqual(12, detailedArgs1.NewValue);
                    Assert.AreEqual(0, detailedArgs1.OldValue);

                    // Wait a little to make sure that the event handler has been removed on the server side as well.
                    await Task.Delay(200);
                    Assert.IsFalse(serviceImpl1.HasDetailedValueChangedHandler);
                    Assert.IsFalse(serviceImpl1.HasValueChangedHandler);

                    clientService2.DetailedValueChanged -= detailedHandler2;
                    await ((IRpcService)clientService2).WaitForPendingEventHandlers().DefaultTimeout();
                    await clientService1.SetValueAsync(25).DefaultTimeout();

                    // Verify 2
                    Assert.AreEqual(24, detailedArgs2.NewValue);
                    Assert.AreEqual(0, detailedArgs2.OldValue);

                    // Wait a little to make sure that the event handler has been removed on the server side as well.
                    await Task.Delay(200);
                    Assert.IsFalse(serviceImpl2.HasDetailedValueChangedHandler);
                    Assert.IsFalse(serviceImpl2.HasValueChangedHandler);
                }
            }
            finally
            {
                await host.ShutdownAsync().DefaultTimeout();
            }
        }

        [Test]
        public async Task MultiInstanceServicesTest()
        {
            var serviceRegistrator = new RpcServiceDefinitionBuilder();
            serviceRegistrator
                .RegisterService<IBlockingService>()
                .RegisterService<ISimpleService>();

            var (host, connection) = this.CreateServerAndConnection(serviceRegistrator);
            var servicePublisher = host.ServicePublisher;
            //RpcServerId rpcServerId = servicePublisher.HostId;
            host.Start();

            try
            {
                var serviceImpl = new TestBlockingSimpleServiceImpl();
                using (var publishScope = servicePublisher.PublishInstance(serviceImpl))
                {
                    var objectId = publishScope.Value.ObjectId;

                    var blockingService = connection.GetServiceInstance<IBlockingServiceClient>(objectId);
                    var simpleService = connection.GetServiceInstance<ISimpleService>(objectId);
                    // Service proxies should be equal, but not necessarily the same
                    Assert.AreEqual(blockingService, simpleService);

                    var blockingService2 = connection.GetServiceInstance<IBlockingServiceClient>(objectId);
                    // Service proxies for the object and service should be the same
                    Assert.AreSame(blockingService, blockingService2);

                    await connection.ShutdownAsync().DefaultTimeout();
                }
            }
            finally
            {
                await host.ShutdownAsync().DefaultTimeout();
            }
        }

        [Test]
        public async Task MultiSingletonServicesTest()
        {
            var serverBuilder = new RpcServiceDefinitionBuilder();
            serverBuilder.RegisterService<ISimpleService>();
            serverBuilder.RegisterService<IBlockingService>();

            var (host, connection) = this.CreateServerAndConnection(serverBuilder);
            var servicePublisher = host.ServicePublisher;
            host.Start();

            try
            {
                var simpleServiceImpl = new TestSimpleServiceImpl();
                var blockingServiceImpl = new TestBlockingServiceImpl();
                using (var publishScope = servicePublisher.PublishSingleton<ISimpleService>(simpleServiceImpl))
                using (var publishScope2 = servicePublisher.PublishSingleton<IBlockingService>(blockingServiceImpl))
                {
                    var clientService = connection.GetServiceSingleton<ISimpleService>();
                    var blockingClientService = connection.GetServiceSingleton<IBlockingService>();
                    var clientService2 = connection.GetServiceSingleton<ISimpleService>();
                    Assert.AreSame(clientService, clientService2);

                    int res = await clientService.AddAsync(8, 9);
                    Assert.AreEqual(8 + 9, res);

                    int res2 = await clientService.AddAsync(12, 13);
                    Assert.AreEqual(12 + 13, res2);
                }
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }

        [Test]
        public async Task PlainEventHandlerTest()
        {
            var serverBuilder = new RpcServiceDefinitionBuilder();
            serverBuilder.RegisterService<ISimpleServiceWithEvents>();

            var (host, connection) = this.CreateServerAndConnection(serverBuilder);
            var servicePublisher = host.ServicePublisher;
            host.Start();

            try
            {
                var serviceImpl1 = new TestServiceWithEventsImpl();
                var serviceImpl2 = new TestServiceWithEventsImpl();

                using (var publishScope1 = servicePublisher.PublishInstance(serviceImpl1))
                using (var publishScope2 = servicePublisher.PublishInstance(serviceImpl2))
                {
                    var objectId1 = publishScope1.Value.ObjectId;
                    var objectId2 = publishScope2.Value.ObjectId;

                    var clientService1 = connection.GetServiceInstance<ISimpleServiceWithEvents>(objectId1);
                    var clientService2 = connection.GetServiceInstance<ISimpleServiceWithEvents>(objectId2);

                    var valueChangedTcs1 = new TaskCompletionSource<EventArgs>();
                    EventHandler valueChangedHandler1 = (s, e) =>
                    {
                        valueChangedTcs1.SetResult(e);
                    };

                    var valueChangedTcs2 = new TaskCompletionSource<EventArgs>();
                    EventHandler valueChangedHandler2 = (s, e) =>
                    {
                        valueChangedTcs2.SetResult(e);
                    };

                    clientService1.ValueChanged += valueChangedHandler1;
                    clientService2.ValueChanged += valueChangedHandler2;

                    await ((IRpcService)clientService1).WaitForPendingEventHandlers();
                    await ((IRpcService)clientService2).WaitForPendingEventHandlers();

                    clientService1.SetValueAsync(12).Forget();
                    clientService2.SetValueAsync(24).Forget();

                    await Task.WhenAll(valueChangedTcs1.Task, valueChangedTcs2.Task).DefaultTimeout();
                    Assert.IsTrue(valueChangedTcs1.Task.IsCompletedSuccessfully());
                    Assert.IsTrue(valueChangedTcs2.Task.IsCompletedSuccessfully());

                    clientService1.ValueChanged -= valueChangedHandler1;
                    await ((IRpcService)clientService1).WaitForPendingEventHandlers();
                    clientService1.SetValueAsync(13).Forget();

                    // Wait a little to make sure that the event handler has been removed on the server side as well.
                    await Task.Delay(200);
                    Assert.IsFalse(serviceImpl1.HasDetailedValueChangedHandler);
                    Assert.IsFalse(serviceImpl1.HasValueChangedHandler);

                    clientService2.ValueChanged -= valueChangedHandler2;
                    await ((IRpcService)clientService2).WaitForPendingEventHandlers();
                    clientService2.SetValueAsync(25).Forget();

                    // Wait a little to make sure that the event handler has been removed on the server side as well.
                    await Task.Delay(200);
                    Assert.IsFalse(serviceImpl2.HasDetailedValueChangedHandler);
                    Assert.IsFalse(serviceImpl2.HasValueChangedHandler);

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
            serverBuilder.RegisterService<IImplicitServiceProviderService>()
                .RegisterService<ISimpleService>()
                .RegisterService<IBlockingService>();

            var (host, connection) = this.CreateServerAndConnection(serverBuilder);
            var servicePublisher = host.ServicePublisher;
            var rpcServerId = servicePublisher.ServerId;

            host.Start();

            try
            {
                var serviceImpl = new ImplicitServiceProviderServiceImpl(host.ServicePublisher);

                using (var publishScope = servicePublisher.PublishSingleton<IImplicitServiceProviderService>(serviceImpl))
                {
                    var clientService = connection.GetServiceSingleton<IImplicitServiceProviderServiceClient>();
                    var serviceRef = await clientService.GetBlockingServiceAsync(0);
                    var blockingService = connection.GetServiceInstance(serviceRef);
                    var blockingService2 = clientService.GetBlockingService(0);
                    Assert.AreEqual(blockingService, blockingService2);

                    int res = await blockingService.AddAsync(8, 9);

                    Assert.AreEqual(8 + 9, res);
                }
            }
            finally
            {
                await host.ShutdownAsync();
            }

        }

        [Test]
        public async Task SingletonServiceTest()
        {
            var serverBuilder = new RpcServiceDefinitionBuilder();
            serverBuilder.RegisterService<ISimpleService>();

            var (host, connection) = this.CreateServerAndConnection(serverBuilder);
            var servicePublisher = host.ServicePublisher;
            host.Start();

            try
            {
                var serviceImpl = new TestSimpleServiceImpl();
                using (var publishScope = servicePublisher.PublishSingleton<ISimpleService>(serviceImpl))
                {
                    var clientService = connection.GetServiceSingleton<ISimpleService>();

                    int res = await clientService.AddAsync(8, 9);

                    Assert.AreEqual(8 + 9, res);
                }
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }

        [Test]
        [Ignore("Not implemented yet.")]
        public async Task UnserializableEventHandlerTest()
        {
            var serverBuilder = new RpcServiceDefinitionBuilder();
            serverBuilder.RegisterService<IServiceWithUnserializableEvent>();

            var (host, connection) = this.CreateServerAndConnection(serverBuilder);
            var servicePublisher = host.ServicePublisher;
            host.Start();

            try
            {
                var serviceMock = new Mock<IServiceWithUnserializableEvent>();

                using (var publishScope = servicePublisher.PublishInstance(serviceMock.Object))
                {
                    var objectId = publishScope.Value.ObjectId;
                    var clientService = connection.GetServiceInstance<IServiceWithUnserializableEvent>(objectId);

                    TaskCompletionSource<UnserializableEventArgs> detailedTcs = new TaskCompletionSource<UnserializableEventArgs>();
                    EventHandler<UnserializableEventArgs> detailedHandler = (s, e) =>
                    {
                        detailedTcs.SetResult(e);
                    };

                    clientService.UnserializableValueChanged += detailedHandler;

                    Assert.ThrowsAsync<RpcFailureException>(((IRpcService)clientService).WaitForPendingEventHandlers);
                }
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }

        private static async Task<ValueChangedEventArgs> TestMixedEventHandlers(ISimpleServiceWithEvents clientService)
        {
            double oldValue = await clientService.GetValueAsync();

            TaskCompletionSource<ValueChangedEventArgs> detailedTcs = new TaskCompletionSource<ValueChangedEventArgs>();
            EventHandler<ValueChangedEventArgs> detailedHandler = (s, e) =>
            {
                detailedTcs.SetResult(e);
            };

            clientService.DetailedValueChanged += detailedHandler;

            var valueChangedTcs = new TaskCompletionSource<EventArgs>();
            EventHandler valueChangedHandler = (s, e) =>
            {
                valueChangedTcs.SetResult(e);
            };

            clientService.ValueChanged += valueChangedHandler;

            await ((IRpcService)clientService).WaitForPendingEventHandlers();

            clientService.SetValueAsync(12).Forget();

            var eventsTask = Task.WhenAll(detailedTcs.Task, valueChangedTcs.Task);
            var completedTask = await Task.WhenAny(eventsTask, Task.Delay(1000));
            Assert.AreEqual(eventsTask, completedTask);
            Assert.IsTrue(completedTask.IsCompletedSuccessfully());

            var detailedArgs = detailedTcs.Task.Result;

            clientService.DetailedValueChanged -= detailedHandler;
            clientService.ValueChanged -= valueChangedHandler;
            await ((IRpcService)clientService).WaitForPendingEventHandlers();

            clientService.SetValueAsync(15).Forget();
            await Task.Delay(200);  // Give some time to allow any incorrect events to be deliverd.

            Assert.AreEqual(12, detailedArgs.NewValue);
            Assert.AreEqual(oldValue, detailedArgs.OldValue);

            return detailedArgs;
        }
    }
}
