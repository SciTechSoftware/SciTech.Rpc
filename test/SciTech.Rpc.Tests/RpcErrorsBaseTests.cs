using NUnit.Framework;
using SciTech.Rpc.Client;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server;
using SciTech.Threading;
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests
{
    /// <summary>
    /// Specifies how an RPC should be performed, first part indicates client side and second part the host side.
    /// So AsyncBlocking indicates an async client and a blocking host, and BlockingAsync indicates a blocking client
    /// and an async host.
    /// </summary>
    public enum TestOperationType
    {
        BlockingBlocking,
        AsyncBlocking,
        BlockingAsync,
        BlockingBlockingVoid,
        AsyncBlockingVoid,
        AsyncAsync,
        AsyncAsyncVoid,
        BlockingAsyncVoid,
    }

    public abstract class RpcErrorsBaseTests : ClientServerTestsBase
    {
        //[Test]
        //public async Task UnknownExceptionTest()
        //{
        //    var serviceRegistrator = new RpcServiceDefinitionBuilder();
        //    serviceRegistrator
        //        .RegisterService<IFaultService>();
        //    var (host, connection) = this.CreateServerAndConnection(serviceRegistrator);
        //    var publishedInstanceScope = host.ServicePublisher.PublishInstance<IFaultService>(new FaultServiceImpl());
        //    var faultService = connection.GetServiceInstance<IFaultService>(publishedInstanceScope.Value);
        //    host.Start();
        //    try
        //    {
        //        var faultException = Assert.Throws<RpcFailureException>(() => faultService.GenerateUnknownException());
        //        Assert.NotNull(faultException);
        //    }
        //    finally
        //    {
        //        await host.ShutdownAsync();
        //    }
        //}
        protected static readonly TestOperationType[] OperationTypes = {
            TestOperationType.BlockingBlocking,
            TestOperationType.AsyncBlocking,
            TestOperationType.BlockingAsync,
            TestOperationType.BlockingBlockingVoid,
            TestOperationType.AsyncBlockingVoid,
            TestOperationType.AsyncAsync,
            TestOperationType.AsyncAsyncVoid
        };

        protected RpcErrorsBaseTests(IRpcSerializer serializer, RpcConnectionType connectionType) : base(serializer, connectionType)
        {
        }

        [Test]
        public async Task CustomExceptionConverterTest()
        {
            var serviceRegistrator = new RpcServiceDefinitionsBuilder();
            serviceRegistrator
                .RegisterService<IDeclaredFaultsService>();
                //.RegisterExceptionConverter(new DeclaredFaultExceptionConverter());

            var (host, connection) = this.CreateServerAndConnection(serviceRegistrator, 
                so=>so.ExceptionConverters.Add(new DeclaredFaultExceptionConverter()), 
                co=>co.ExceptionConverters.Add(new DeclaredFaultExceptionConverter())
                );
            var publishedInstanceScope = host.ServicePublisher.PublishInstance<IDeclaredFaultsService>(new FaultServiceImpl());

            var faultService = connection.GetServiceInstance<IDeclaredFaultsService>(publishedInstanceScope.Value);
            host.Start();
            try
            {
                var faultException = Assert.Throws<DeclaredFaultException>(() => faultService.GenerateCustomDeclaredExceptionAsync());
                Assert.IsNotEmpty(faultException.Message);
                Assert.IsNotEmpty(faultException.DetailedMessage);
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }

        [TestCaseSource(nameof(OperationTypes))]
        public async Task DeclaredFault_ShouldThrowRpcFaultException(TestOperationType operationType)
        {
            var serviceRegistrator = new RpcServiceDefinitionsBuilder();
            serviceRegistrator
                .RegisterService<IDeclaredFaultsService>();

            var (host, connection) = this.CreateServerAndConnection(serviceRegistrator);
            try
            {
                host.Start();

                using (var publishedInstanceScope = host.ServicePublisher.PublishInstance<IDeclaredFaultsService>(new FaultServiceImpl()))
                {

                    var faultService = connection.GetServiceInstance<IFaultServiceClient>(publishedInstanceScope.Value);

                    // Invoke unknown service instance
                    switch (operationType)
                    {
                        case TestOperationType.BlockingBlocking:
                            Assert.Throws<RpcFaultException<DeclaredFault>>(() => faultService.GenerateDeclaredFault(12));
                            break;
                        case TestOperationType.AsyncBlocking:
                            Assert.ThrowsAsync<RpcFaultException<DeclaredFault>>(() => faultService.GenerateDeclaredFaultAsync(12));
                            break;
                        case TestOperationType.BlockingAsync:
                            Assert.Throws<RpcFaultException<DeclaredFault>>(() => faultService.GenerateAsyncDeclaredFault(true));
                            Assert.Throws<RpcFaultException<DeclaredFault>>(() => faultService.GenerateAsyncDeclaredFault(false));
                            break;
                        case TestOperationType.AsyncAsync:
                            Assert.ThrowsAsync<RpcFaultException<DeclaredFault>>(() => faultService.GenerateAsyncDeclaredFaultAsync(true));
                            Assert.ThrowsAsync<RpcFaultException<DeclaredFault>>(() => faultService.GenerateAsyncDeclaredFaultAsync(false));
                            break;
                        case TestOperationType.BlockingBlockingVoid:
                            Assert.Throws<RpcFaultException<AnotherDeclaredFault>>(() => faultService.GenerateAnotherDeclaredFault(12));
                            break;
                        case TestOperationType.AsyncBlockingVoid:
                            Assert.ThrowsAsync<RpcFaultException<AnotherDeclaredFault>>(() => faultService.GenerateAnotherDeclaredFaultAsync(12));
                            break;
                        case TestOperationType.BlockingAsyncVoid:
                            Assert.Throws<RpcFaultException<DeclaredFault>>(() => faultService.GenerateAsyncAnotherDeclaredFault(true));
                            Assert.Throws<RpcFaultException<AnotherDeclaredFault>>(() => faultService.GenerateAsyncAnotherDeclaredFault(false));
                            break;

                        case TestOperationType.AsyncAsyncVoid:
                            Assert.ThrowsAsync<RpcFaultException<DeclaredFault>>(() => faultService.GenerateAsyncAnotherDeclaredFaultAsync(true));
                            Assert.ThrowsAsync<RpcFaultException<AnotherDeclaredFault>>(() => faultService.GenerateAsyncAnotherDeclaredFaultAsync(false));
                            break;
                        default:
                            throw new NotImplementedException(operationType.ToString());
                    }

                    // Make sure that the connection is still usable after exception
                    Assert.AreEqual(12 + 13, faultService.Add(12, 13));
                }
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }

        [Test]
        public async Task DeclaredServiceFaultTest()
        {
            var serviceRegistrator = new RpcServiceDefinitionsBuilder();
            serviceRegistrator
                .RegisterService<IServiceFaultService>();

            var (host, connection) = this.CreateServerAndConnection(serviceRegistrator);
            try
            {
                host.Start();

                using (var publishedInstanceScope = host.ServicePublisher.PublishInstance<IServiceFaultService>(new ServiceFaultServiceImpl()))
                {
                    var faultService = connection.GetServiceInstance<IServiceFaultServiceClient>(publishedInstanceScope.Value);

                    Assert.Throws<RpcFaultException<AnotherServiceDeclaredFault>>(() => faultService.GenerateServiceFault(true, true));
                    Assert.Throws<RpcFaultException<AnotherDeclaredFault>>(() => faultService.GenerateServiceFault(true, false));
                    Assert.Throws<RpcFaultException<ServiceDeclaredFault>>(() => faultService.GenerateServiceFault(false, true));
                    Assert.Throws<RpcFaultException<DeclaredFault>>(() => faultService.GenerateServiceFault(false, false));

                    // Make sure that the connection is still usable after exception
                    Assert.AreEqual(12 + 13, faultService.Add(12, 13));
                }
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }

        [Test]
        public void HostNotFoundTest()
        {
            var serviceRegistrator = new RpcServiceDefinitionsBuilder();
            serviceRegistrator
                .RegisterService<IBlockingService>(); ;

            var (_, connection) = this.CreateServerAndConnection(serviceRegistrator);

            var objectId = RpcObjectId.NewId();
            var clientService = connection.GetServiceInstance<IBlockingServiceClient>(objectId);
            // Invoke client operation without starting host
            Assert.Throws<RpcCommunicationException>(() => clientService.Add(12, 13));
        }

        [Test]
        public void IncorrectFaultOp_ShouldThrowDefinitionException()
        {
            var serviceRegistrator = new RpcServiceDefinitionsBuilder();

            var (_, connection) = this.CreateServerAndConnection(serviceRegistrator);

            Assert.Throws<RpcDefinitionException>(() => connection.GetServiceInstance<IIncorrectFaultOpServiceClient>(RpcObjectId.NewId()));
        }

        [Test]
        public void IncorrectServiceFault_ShouldThrowDefinitionException()
        {
            var serviceRegistrator = new RpcServiceDefinitionsBuilder();

            var (_, connection) = this.CreateServerAndConnection(serviceRegistrator);

            Assert.Throws<RpcDefinitionException>(() => connection.GetServiceInstance<IIncorrectServiceFaultServiceClient>(RpcObjectId.NewId()));
        }

        [Test]
        public async Task NoDetailsDeclaredFaultTest()
        {
            var serviceRegistrator = new RpcServiceDefinitionsBuilder();
            serviceRegistrator
                .RegisterService<IDeclaredFaultsService>();

            var (host, connection) = this.CreateServerAndConnection(serviceRegistrator);
            try
            {
                host.Start();

                using (var publishedInstanceScope = host.ServicePublisher.PublishInstance<IDeclaredFaultsService>(new FaultServiceImpl()))
                {
                    var faultService = connection.GetServiceInstance<IFaultServiceClient>(publishedInstanceScope.Value);

                    var faultException = Assert.Throws<RpcFaultException>(() => faultService.GenerateNoDetailsFault());
                    Assert.AreEqual("NoDetailsFault", faultException.FaultCode);

                    // Make sure that the connection is still usable after exception
                    Assert.AreEqual(12 + 13, faultService.Add(12, 13));
                }
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }

        [Test]
        public async Task TooLargeClientMessage_ShouldThrowException()
        {
            var definitionBuilder = new RpcServiceDefinitionsBuilder();
            definitionBuilder.RegisterService<ISimpleService>();

            var (host, connection) = this.CreateServerAndConnection(definitionBuilder, options =>
                {
                    options.ReceiveMaxMessageSize = 10000;
                });
            host.Start();
            try
            {
                using (var publishedInstanceScope = host.ServicePublisher.PublishInstance<ISimpleService>(new TestSimpleServiceImpl()))
                {
                    var simpleService = connection.GetServiceInstance(publishedInstanceScope.Value);

                    int[] data = new int[10000];
                    for (int i = 0; i < data.Length; i++)
                    {
                        data[i] = i;
                    }

                    // TODO: Lightweight will throw RpcCommunicationException (due to disconnect) and 
                    // gRPC will throw RpcFailureException. What's the correct exception?
                    var sumTask = simpleService.SumAsync(data).DefaultTimeout();
                    Assert.ThrowsAsync(Is.TypeOf<RpcFailureException>().Or.TypeOf<RpcCommunicationException>(), () => sumTask);

                    // TODO: gRPC keeps the connection alive. Should the Lightweight connection
                    // also be kept alive (far from trivial)?
                    if (this.ConnectionType == RpcConnectionType.Grpc)
                    {
                        // Verify that service is still available.
                        int res = simpleService.AddAsync(5, 6).AwaiterResult();
                        Assert.AreEqual(5 + 6, res);
                    }
                }

            }
            finally
            {
                await host.ShutdownAsync();
            }
        }

        [Test]
        public void TooLargeServerMessage_ShouldThrowException()
        {
            var definitionBuilder = new RpcServiceDefinitionsBuilder();
            definitionBuilder.RegisterService<ISimpleService>();

            var (host, connection) = this.CreateServerAndConnection(definitionBuilder, options =>
            {
                options.SendMaxMessageSize = 10000;
            });
            host.Start();
            try
            {
                using (host.ServicePublisher.PublishSingleton<ISimpleService>(new TestSimpleServiceImpl()))
                {
                    var simpleService = connection.GetServiceSingleton<ISimpleService>();
                    // TODO: Shouldn't be RpcFailureException.
                    Assert.ThrowsAsync<RpcFailureException>(() => simpleService.GetArrayAsync(10000).DefaultTimeout());

                    // Verify that service is still available.
                    int res = simpleService.AddAsync(5, 6).Result;
                    Assert.AreEqual(5 + 6, res);
                }

            }
            finally
            {
                host.ShutdownAsync();
            }
        }

        [TestCaseSource(nameof(OperationTypes))]
        public async Task UnavailableService_ShouldThrowRpcServiceUnavailableException(TestOperationType operationType)
        {
            var serviceRegistrator = new RpcServiceDefinitionsBuilder();
            serviceRegistrator
                .RegisterService<IBlockingService>()
                .RegisterService<ISimpleService>();

            var (host, connection) = this.CreateServerAndConnection(serviceRegistrator);
            using (var publishedInstanceScope = host.ServicePublisher.PublishInstance<IBlockingService>(new TestBlockingServiceImpl()))
            {
                var objectId = RpcObjectId.NewId();
                var blockingClientService = connection.GetServiceInstance<IBlockingServiceClient>(objectId);
                var simpleService = connection.GetServiceInstance<ISimpleService>(objectId);
                host.Start();

                try
                {
                    // Invoke unknown service instance
                    switch (operationType)
                    {
                        case TestOperationType.BlockingBlocking:
                            Assert.Throws<RpcServiceUnavailableException>(() => blockingClientService.Add(12, 13));
                            break;
                        case TestOperationType.AsyncBlocking:
                            // Async/blocking (client/host)
                            Assert.ThrowsAsync<RpcServiceUnavailableException>(() => blockingClientService.AddAsync(12, 13));
                            break;
                        case TestOperationType.BlockingBlockingVoid:
                            Assert.Throws<RpcServiceUnavailableException>(() => blockingClientService.Value = 23);
                            break;
                        case TestOperationType.AsyncBlockingVoid:
                            // Async/blocking void (client/host)
                            Assert.ThrowsAsync<RpcServiceUnavailableException>(() => blockingClientService.SetValueAsync(13));
                            break;

                        case TestOperationType.AsyncAsync:
                            // Async/async (client/host)
                            Assert.ThrowsAsync<RpcServiceUnavailableException>(() => simpleService.AddAsync(12, 13));
                            break;
                        case TestOperationType.AsyncAsyncVoid:
                            // Async/async void (client/host)
                            Assert.ThrowsAsync<RpcServiceUnavailableException>(() => simpleService.SetValueAsync(12));
                            break;
                    }

                    var exisingService = connection.GetServiceInstance<IBlockingServiceClient>(publishedInstanceScope.Value);
                    // Make sure that the connection is still usable after exception
                    Assert.AreEqual(12 + 13, exisingService.Add(12, 13));

                }
                finally
                {
                    await host.ShutdownAsync();
                }
            }
        }

        [TestCaseSource(nameof(OperationTypes))]
        public async Task UndeclaredException_ShouldThrowRpcFailure(TestOperationType operationType)
        {
            var serviceRegistrator = new RpcServiceDefinitionsBuilder();
            serviceRegistrator
                .RegisterService<IDeclaredFaultsService>();


            var (host, connection) = this.CreateServerAndConnection(serviceRegistrator);
            var publishedInstanceScope = host.ServicePublisher.PublishInstance<IDeclaredFaultsService>(new FaultServiceImpl());

            var faultService = connection.GetServiceInstance<IFaultServiceClient>(publishedInstanceScope.Value);
            host.Start();
            try
            {
                switch (operationType)
                {
                    case TestOperationType.AsyncAsyncVoid:
                        Assert.ThrowsAsync<RpcFailureException>(() => faultService.GenerateUndeclaredAsyncExceptionAsync(false).DefaultTimeout());
                        break;
                    case TestOperationType.AsyncAsync:
                        Assert.ThrowsAsync<RpcFailureException>(() => faultService.GenerateUndeclaredAsyncExceptionWithReturnAsync(false).DefaultTimeout());
                        break;
                    case TestOperationType.BlockingAsyncVoid:
                        Assert.Throws<RpcFailureException>(() => faultService.GenerateUndeclaredAsyncException(false));
                        break;
                    case TestOperationType.BlockingAsync:
                        Assert.Throws<RpcFailureException>(() => faultService.GenerateUndeclaredAsyncExceptionWithReturn(false));
                        break;

                    case TestOperationType.AsyncBlockingVoid:
                        Assert.ThrowsAsync<RpcFailureException>(() => faultService.GenerateUndeclaredExceptionAsync().DefaultTimeout());
                        break;
                    case TestOperationType.AsyncBlocking:
                        Assert.ThrowsAsync<RpcFailureException>(() => faultService.GenerateUndeclaredExceptionWithReturnAsync().DefaultTimeout());
                        break;
                    case TestOperationType.BlockingBlockingVoid:
                        Assert.Throws<RpcFailureException>(() => faultService.GenerateUndeclaredException());
                        break;
                    case TestOperationType.BlockingBlocking:
                        Assert.Throws<RpcFailureException>(() => faultService.GenerateUndeclaredExceptionWithReturn());
                        break;
                }

                // TODO: Should this be allowed, or should the connection be closed on undeclared exceptions?
                int res = faultService.Add(5, 6);
                Assert.AreEqual(5 + 6, res);
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }

        [Test]
        public async Task UndeclaredFaultException_ShouldThrowFault()
        {
            var serviceRegistrator = new RpcServiceDefinitionsBuilder();
            serviceRegistrator
                .RegisterService<IDeclaredFaultsService>();

            var (host, connection) = this.CreateServerAndConnection(serviceRegistrator);
            var publishedInstanceScope = host.ServicePublisher.PublishInstance<IDeclaredFaultsService>(new FaultServiceImpl());

            var faultService = connection.GetServiceInstance<IDeclaredFaultsService>(publishedInstanceScope.Value);
            host.Start();
            try
            {
                var faultException = Assert.ThrowsAsync<RpcFaultException>(() => faultService.GenerateUndeclaredFaultExceptionAsync(false));
                Assert.AreEqual("AnotherDeclaredFault", faultException.FaultCode);
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }
        //public static Delegate[] TestFuncs = new Delegate[]
        //{
        //    (IBlockingServiceClient blockingService)=>blockingService.Add(12, 13);
        //};

        [Test]
        public async Task ServiceFaultConverter_ShouldConvert()
        {
            var serviceRegistrator = new RpcServiceDefinitionsBuilder();
            serviceRegistrator
                .RegisterService<IMathFaultsService>();

            var (host, connection) = this.CreateServerAndConnection(serviceRegistrator);
            var publishedInstanceScope = host.ServicePublisher.PublishInstance<IMathFaultsService>(new MathFaultsServiceImpl());

            var faultService = connection.GetServiceInstance(publishedInstanceScope.Value);
            host.Start();
            try
            {
                Assert.Throws<MathException>(() => faultService.Sqrt(-5));
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }

        [Test]
        public async Task OperationFaultConverter_ShouldOverride_ServiceFault()
        {
            var serviceRegistrator = new RpcServiceDefinitionsBuilder();
            serviceRegistrator
                .RegisterService<IMathFaultsService>();

            var (host, connection) = this.CreateServerAndConnection(serviceRegistrator);
            var publishedInstanceScope = host.ServicePublisher.PublishInstance<IMathFaultsService>(new MathFaultsServiceImpl());

            var faultService = connection.GetServiceInstance(publishedInstanceScope.Value);
            host.Start();
            try
            {
                Assert.Throws<DivideByZeroException>(() => faultService.Divide(5,0));
                // Make sure that the connection is still usable after exception
                Assert.AreEqual(2, faultService.Divide(10, 5));
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }
    }
}
