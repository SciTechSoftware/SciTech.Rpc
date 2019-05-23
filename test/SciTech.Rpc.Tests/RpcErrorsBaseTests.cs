using NUnit.Framework;
using SciTech.Rpc.Client;
using SciTech.Rpc.Server;
using SciTech.Rpc.Tests.Pipelines;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests
{
    public abstract class RpcErrorsBaseTests : ClientServerTestsBase
    {

        protected RpcErrorsBaseTests(IRpcSerializer serializer, RpcConnectionType connectionType) : base(serializer, connectionType)
        {
        }

        [Test]
        public void HostNotFoundTest()
        {
            var serviceRegistrator = new RpcServiceDefinitionBuilder();
            serviceRegistrator
                .RegisterService<IBlockingService>(); ;

            var (host, connection) = this.CreateServerAndConnection(serviceRegistrator);

            var objectId = RpcObjectId.NewId();
            var clientService = connection.GetServiceInstance<IBlockingServiceClient>(objectId);
            // Invoke client operation without starting host
            Assert.Throws<RpcCommunicationException>(()=>clientService.Add(12, 13));
        }

        [TestCaseSource(nameof(OperationTypes))]
        public async Task ServiceUnavailableTest(TestOperationType operationType)
        {
            var serviceRegistrator = new RpcServiceDefinitionBuilder();
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
        public async Task DeclaredFaultTest(TestOperationType operationType)
        {
            var serviceRegistrator = new RpcServiceDefinitionBuilder();
            serviceRegistrator
                .RegisterService<IFaultService>();

            var (host, connection) = this.CreateServerAndConnection(serviceRegistrator);
            try
            {
                host.Start();

                using (var publishedInstanceScope = host.ServicePublisher.PublishInstance<IFaultService>(new FaultServiceImpl()))
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
        public async Task NoDetailsDeclaredFaultTest()
        {
            var serviceRegistrator = new RpcServiceDefinitionBuilder();
            serviceRegistrator
                .RegisterService<IFaultService>();

            var (host, connection) = this.CreateServerAndConnection(serviceRegistrator);
            try
            {
                host.Start();

                using (var publishedInstanceScope = host.ServicePublisher.PublishInstance<IFaultService>(new FaultServiceImpl()))
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
        public async Task DeclaredServiceFaultTest()
        {
            var serviceRegistrator = new RpcServiceDefinitionBuilder();
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

                    //// Invoke unknown service instance
                    //switch (operationType)
                    //{
                    //    case TestOperationType.BlockingBlocking:
                    //        Assert.Throws<RpcFaultException<DeclaredFault>>(() => faultService.GenerateDeclaredFault(12));
                    //        break;
                    //    case TestOperationType.AsyncBlocking:
                    //        Assert.ThrowsAsync<RpcFaultException<DeclaredFault>>(() => faultService.GenerateDeclaredFaultAsync(12));
                    //        break;
                    //    case TestOperationType.BlockingAsync:
                    //        Assert.Throws<RpcFaultException<DeclaredFault>>(() => faultService.GenerateAsyncDeclaredFault(true));
                    //        Assert.Throws<RpcFaultException<DeclaredFault>>(() => faultService.GenerateAsyncDeclaredFault(false));
                    //        break;
                    //    case TestOperationType.AsyncAsync:
                    //        Assert.ThrowsAsync<RpcFaultException<DeclaredFault>>(() => faultService.GenerateAsyncDeclaredFaultAsync(true));
                    //        Assert.ThrowsAsync<RpcFaultException<DeclaredFault>>(() => faultService.GenerateAsyncDeclaredFaultAsync(false));
                    //        break;
                    //    case TestOperationType.BlockingBlockingVoid:
                    //        Assert.Throws<RpcFaultException<AnotherDeclaredFault>>(() => faultService.GenerateAnotherDeclaredFault(12));
                    //        break;
                    //    case TestOperationType.AsyncBlockingVoid:
                    //        Assert.ThrowsAsync<RpcFaultException<AnotherDeclaredFault>>(() => faultService.GenerateAnotherDeclaredFaultAsync(12));
                    //        break;
                    //    case TestOperationType.BlockingAsyncVoid:
                    //        Assert.Throws<RpcFaultException<DeclaredFault>>(() => faultService.GenerateAsyncAnotherDeclaredFault(true));
                    //        Assert.Throws<RpcFaultException<AnotherDeclaredFault>>(() => faultService.GenerateAsyncAnotherDeclaredFault(false));
                    //        break;

                    //    case TestOperationType.AsyncAsyncVoid:
                    //        Assert.ThrowsAsync<RpcFaultException<DeclaredFault>>(() => faultService.GenerateAsyncAnotherDeclaredFaultAsync(true));
                    //        Assert.ThrowsAsync<RpcFaultException<AnotherDeclaredFault>>(() => faultService.GenerateAsyncAnotherDeclaredFaultAsync(false));
                    //        break;
                    //}

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
        public async Task CustomExceptionConverterTest()
        {
            var serviceRegistrator = new RpcServiceDefinitionBuilder();
            serviceRegistrator
                .RegisterService<IFaultService>()
                .RegisterExceptionConverter(new DeclaredFaultExceptionConverter());

            var proxyServicesProvider = new RpcProxyServicesBuilder();
            proxyServicesProvider.RegisterExceptionConverter(new DeclaredFaultExceptionConverter());

            var (host, connection) = this.CreateServerAndConnection(serviceRegistrator, proxyServicesProvider);
            var publishedInstanceScope = host.ServicePublisher.PublishInstance<IFaultService>(new FaultServiceImpl());

            var faultService = connection.GetServiceInstance<IFaultService>(publishedInstanceScope.Value);
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

        [Test]
        public async Task UndeclaredExceptionTest()
        {
            var serviceRegistrator = new RpcServiceDefinitionBuilder();
            serviceRegistrator
                .RegisterService<IFaultService>();
               

            var (host, connection) = this.CreateServerAndConnection(serviceRegistrator);
            var publishedInstanceScope = host.ServicePublisher.PublishInstance<IFaultService>(new FaultServiceImpl());

            var faultService = connection.GetServiceInstance<IFaultService>(publishedInstanceScope.Value);
            host.Start();
            try
            {
                // TODO: Fault or failure exception here?
                Assert.ThrowsAsync<RpcFaultException>(() => faultService.GenerateUndeclaredExceptionAsync(false));
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }

        [Test]
        public async Task UndeclaredFaultExceptionTest()
        {
            var serviceRegistrator = new RpcServiceDefinitionBuilder();
            serviceRegistrator
                .RegisterService<IFaultService>();

            var (host, connection) = this.CreateServerAndConnection(serviceRegistrator);
            var publishedInstanceScope = host.ServicePublisher.PublishInstance<IFaultService>(new FaultServiceImpl());

            var faultService = connection.GetServiceInstance<IFaultService>(publishedInstanceScope.Value);
            host.Start();
            try
            {
                var faultException = Assert.ThrowsAsync<RpcFaultException>(()=>faultService.GenerateUndeclaredFaultExceptionAsync(false) );
                Assert.AreEqual("", faultException.FaultCode);
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }

        protected static readonly TestOperationType[] OperationTypes = {
            TestOperationType.BlockingBlocking,
            TestOperationType.AsyncBlocking,
            TestOperationType.BlockingAsync,
            TestOperationType.BlockingBlockingVoid,
            TestOperationType.AsyncBlockingVoid,
            TestOperationType.AsyncAsync,
            TestOperationType.AsyncAsyncVoid };

        //public static Delegate[] TestFuncs = new Delegate[]
        //{
        //    (IBlockingServiceClient blockingService)=>blockingService.Add(12, 13);
        //};
    }

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
}
