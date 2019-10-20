using NUnit.Framework;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server;
using SciTech.Rpc.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace SciTech.Rpc.Tests
{
    public abstract class ActivationTests : ClientServerTestsBase
    {
        internal static List<ActivationTestService> s_testServices;
        internal static DisposableActivationTestService s_disposableTestService;
        internal static ActivationTestServiceWithDependency s_dependencyTestService;

        protected ActivationTests(RpcConnectionType connectionType) : base(new JsonRpcSerializer(), connectionType)
        {

        }

        [Test]
        public async Task Singleton_Should_CreateOnce()
        {
            var mocks = new List<ActivationTestService>();
            
            void ConfigureServices(IServiceCollection s )
            {
                s.AddSingleton<IActivationTestService>(s =>
                {
                    var serviceImpl = new ActivationTestService();
                    mocks.Add(serviceImpl);
                    return serviceImpl;
                });
            }

            var (host, connection) = CreateServerAndConnection(configureServices: ConfigureServices );

            host.PublishSingleton<IActivationTestService>();
            host.Start();
            try
            {

                var service = connection.GetServiceSingleton<IActivationTestService>();
                service.Test();
                service.Test();

                Assert.AreEqual(1, mocks.Count);
                foreach (var mock in mocks)
                {
                    Assert.AreEqual(2, mock.CallCount);
                }

                await connection.ShutdownAsync();
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }

        [Test]
        public async Task Transient_Should_CreatePerCall()
        {
            var mocks = new List<ActivationTestService>();

            void ConfigureServices(IServiceCollection s)
            {
                s.AddTransient<IActivationTestService>(s =>
                {
                    var serviceImpl = new ActivationTestService();
                    mocks.Add(serviceImpl);
                    return serviceImpl;
                });
            }

            var (host, connection) = CreateServerAndConnection(configureServices: ConfigureServices);

            host.PublishSingleton<IActivationTestService>();
            host.Start();
            try
            {

                var service = connection.GetServiceSingleton<IActivationTestService>();
                service.Test();
                service.Test();

                Assert.AreEqual(2, mocks.Count);
                foreach (var mock in mocks)
                {
                    Assert.AreEqual(1, mock.CallCount);
                }

                await connection.ShutdownAsync();
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }

        [Test]
        public async Task Transient_Should_Dispose()
        {
            DisposableActivationTestService testService = null;

            void ConfigureServices(IServiceCollection s)
            {
                s.AddTransient<IActivationTestService>(s =>
                {
                    testService = new DisposableActivationTestService();
                    return testService;
                });
            }

            var (host, connection) = CreateServerAndConnection(configureServices: ConfigureServices);

            host.PublishSingleton<IActivationTestService>();
            host.Start();
            try
            {
                var service = connection.GetServiceSingleton<IActivationTestService>();
                service.Test();

                Assert.NotNull(testService);
                await testService.WaitForDisposeAsync().DefaultTimeout();

                Assert.AreEqual(1, testService.DisposeCount);

                await connection.ShutdownAsync();
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }

        [Test]
        public async Task Transient_Should_DisposeDependency()
        {
            ActivationTestServiceWithDependency testService = null;

            void ConfigureServices(IServiceCollection s)
            {
                s.AddTransient<IDisposableDependency, DisposableDependency>();
                s.AddTransient<IActivationTestService>(s =>
                {
                    testService = new ActivationTestServiceWithDependency(s.GetService<IDisposableDependency>());
                    return testService;
                });
            }

            var (host, connection) = CreateServerAndConnection(configureServices: ConfigureServices);

            host.PublishSingleton<IActivationTestService>();
            host.Start();
            try
            {

                var service = connection.GetServiceSingleton<IActivationTestService>();
                service.Test();

                var dependency = (DisposableDependency)testService?.Dependency;
                Assert.NotNull(dependency);

                await dependency.WaitForDisposeAsync().DefaultTimeout();
                Assert.AreEqual(1, dependency.DisposeCount);

                await connection.ShutdownAsync();
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }

        [Test]
        public async Task ObjectFactory_Should_Dispose()
        {
            var (host, connection) = CreateServerAndConnection(configureServices: s => { });

            host.PublishSingleton<DisposableActivationTestService, IActivationTestService>();
            host.Start();
            try
            {
                var service = connection.GetServiceSingleton<IActivationTestService>();
                service.Test();

                Assert.NotNull(s_disposableTestService);
                await s_disposableTestService.WaitForDisposeAsync().DefaultTimeout();
                Assert.AreEqual(1, s_disposableTestService.DisposeCount);

                await connection.ShutdownAsync();
            }
            finally
            {
                s_disposableTestService = null;
                await host.ShutdownAsync();
            }
        }

        [Test]
        public async Task ObjectFactory_Should_DisposeDependency()
        {
            void ConfigureServices(IServiceCollection s)
            {
                s.AddTransient<IDisposableDependency, DisposableDependency>();
            }

            var (host, connection) = CreateServerAndConnection(configureServices: ConfigureServices);

            host.PublishSingleton<ActivationTestServiceWithDependency, IActivationTestService>();
            host.Start();
            try
            {
                var service = connection.GetServiceSingleton<IActivationTestService>();
                service.Test();

                var dependency = (DisposableDependency)s_dependencyTestService?.Dependency;
                Assert.NotNull(dependency);

                await dependency.WaitForDisposeAsync().DefaultTimeout();
                Assert.AreEqual(1, dependency.DisposeCount);

                await connection.ShutdownAsync();
            }
            finally
            {
                s_dependencyTestService = null;
                await host.ShutdownAsync();
            }
        }

        [Test]
        public async Task ObjectFactory_Should_CreatePerCall()
        {
            s_testServices = new List<ActivationTestService>();

            var (host, connection) = CreateServerAndConnection(configureServices: s => { });

            host.PublishSingleton<ActivationTestService, IActivationTestService>();
            host.Start();
            try
            {

                var service = connection.GetServiceSingleton<IActivationTestService>();
                service.Test();
                service.Test();

                Assert.AreEqual(2, s_testServices.Count);
                foreach (var mock in s_testServices)
                {
                    Assert.AreEqual(1, mock.CallCount);
                }

                await connection.ShutdownAsync();
            }
            finally
            {
                s_testServices = null;
                await host.ShutdownAsync();
            }
        }
    }

    public interface IActivationTestFactory
    {

    }

    [RpcService]
    public interface IActivationTestService
    {
        void Test();
    }

    public class ActivationTestService : IActivationTestService
    {
        internal int CallCount;

        public ActivationTestService()
        {
            ActivationTests.s_testServices?.Add(this);
        }

        public void Test()
        {
            this.CallCount++;
        }
    }
    public class ActivationTestServiceWithDependency : ActivationTestService
    {
        internal IDisposableDependency Dependency;

        public ActivationTestServiceWithDependency(IDisposableDependency dependency)
        {
            ActivationTests.s_dependencyTestService = this;
            this.Dependency = dependency;
        }
    }
    public interface IDisposableDependency
    {

    }

    public class DisposableDependency : IDisposableDependency, IDisposable
    {
        internal int DisposeCount;
        private TaskCompletionSource<bool> disposeTcs = new TaskCompletionSource<bool>();

        public DisposableDependency()
        {            
        }

        public Task WaitForDisposeAsync() => this.disposeTcs.Task;

        public void Dispose()
        {
            this.DisposeCount++;
            this.disposeTcs.TrySetResult(true);
        }
    }

    public class DisposableActivationTestService : ActivationTestService, IDisposable
    {
        internal int DisposeCount;
        private TaskCompletionSource<bool> disposeTcs = new TaskCompletionSource<bool>();

        public DisposableActivationTestService()
        {
            ActivationTests.s_disposableTestService = this;
        }

        public Task WaitForDisposeAsync() => this.disposeTcs.Task;

        public void Dispose()
        {
            this.DisposeCount++;
            this.disposeTcs.TrySetResult(true);
        }

    }


}
