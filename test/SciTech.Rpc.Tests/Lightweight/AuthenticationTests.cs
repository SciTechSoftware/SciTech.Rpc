using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using SciTech.Rpc.Client;
using SciTech.Rpc.Lightweight.Client;
using SciTech.Rpc.Lightweight.Server;
using SciTech.Rpc.Server;
using SciTech.Threading;
using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Lightweight
{
    public class AuthenticationTests
    {
        [RpcService]
        public interface INegotiateService
        {
            Task<string> GetAsyncIdentityAsync();

            string GetIdentity();

            Task ThrowAsyncIdentity();

            void ThrowIdentity();
        }

        [RpcService(ServerDefinitionType = typeof(INegotiateService))]
        public interface INegotiateServiceClient : INegotiateService
        {
            string GetAsyncIdentity();

            Task<string> GetIdentityAsync();

            Task ThrowAsyncIdentityAsync();

            Task ThrowIdentityAsync();
        }

        [TestCase("LOCALTESTACCOUNT")]
        [TestCase("DOMAINTESTACCOUNT")]
        public async Task AccountNegotiateTest(string accountKey)
        {
            string userName = Environment.GetEnvironmentVariable(FormattableString.Invariant($"RPC_{accountKey}_NAME"));
            string userPw = Environment.GetEnvironmentVariable(FormattableString.Invariant($"RPC_{accountKey}_PASSWORD"));
            string expectedIdentity = Environment.GetEnvironmentVariable(FormattableString.Invariant($"RPC_{accountKey}_IDENTITY"));

            Assert.IsNotEmpty(userName);
            Assert.IsNotEmpty(userPw);
            Assert.IsNotEmpty(expectedIdentity);

            LightweightRpcServer server = CreateServer();

            using var _ = server.PublishSingleton<INegotiateService>();

            server.Start();
            try
            {
                var connection = new TcpRpcConnection(new RpcConnectionInfo(new Uri("lightweight.tcp://localhost:50052")), new NegotiateClientOptions
                {
                    Credential = new NetworkCredential(userName, userPw)
                });

                await connection.ConnectAsync(default).ContextFree();
                Assert.IsTrue(connection.IsConnected);
                Assert.IsTrue(connection.IsEncrypted);
                Assert.IsTrue(connection.IsSigned);

                var negotiateService = connection.GetServiceSingleton<INegotiateService>();
                string identity = negotiateService.GetIdentity();

                Assert.AreEqual(expectedIdentity, identity);
            }
            finally
            {
                await server.ShutdownAsync().ContextFree();
            }
        }

        [TestCase(TestOperationType.BlockingBlocking)]
        [TestCase(TestOperationType.AsyncBlocking)]
        [TestCase(TestOperationType.BlockingAsync)]
        [TestCase(TestOperationType.AsyncAsync)]
        [TestCase(TestOperationType.BlockingBlockingVoid)]
        public async Task DefaultNegotiateTest(TestOperationType operationType)
        {
            if( !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            LightweightRpcServer server = CreateServer();

            using var _ = server.PublishSingleton<INegotiateService>();

            server.Start();
            try
            {
                var connection = new TcpRpcConnection(new RpcConnectionInfo(new Uri("lightweight.tcp://localhost:50052")), new NegotiateClientOptions());

                await connection.ConnectAsync(default).ContextFree();
                Assert.IsTrue(connection.IsConnected);
                Assert.IsTrue(connection.IsEncrypted);
                Assert.IsTrue(connection.IsSigned);

                var negotiateService = connection.GetServiceSingleton<INegotiateServiceClient>();

                string identity = operationType switch
                {
                    TestOperationType.BlockingBlocking => negotiateService.GetIdentity(),
                    TestOperationType.AsyncBlocking => await negotiateService.GetIdentityAsync(),
                    TestOperationType.BlockingAsync => negotiateService.GetAsyncIdentity(),
                    TestOperationType.AsyncAsync => await negotiateService.GetAsyncIdentityAsync(),
                    TestOperationType.BlockingBlockingVoid => Assert.Throws<RpcFaultException>(() => negotiateService.ThrowIdentity()).FaultCode,
                    TestOperationType.AsyncBlockingVoid => Assert.ThrowsAsync<RpcFaultException>(() => negotiateService.ThrowIdentityAsync()).FaultCode,
                    TestOperationType.AsyncAsyncVoid => Assert.ThrowsAsync<RpcFaultException>(() => negotiateService.ThrowAsyncIdentityAsync()).FaultCode,
                    _ => throw new NotImplementedException(operationType.ToString()),
                };

                Assert.AreEqual(WindowsIdentity.GetCurrent()?.Name, identity);

                await connection.DisposeAsync();
            }
            finally
            {
                await server.ShutdownAsync().ContextFree();
            }
        }

        [Test]
        public void MismatchedAuthenication_Should_Throw()
        {
            using var server = new LightweightRpcServer();
            server.AddEndPoint(new TcpRpcEndPoint("127.0.0.1", 50052, false, new SslServerOptions(TestCertificates.ServerCertificate) ));
            server.Start();

            var connection = new TcpRpcConnection(new RpcConnectionInfo(new Uri("lightweight.tcp://localhost:50052")), new NegotiateClientOptions());

            var x = Assert.CatchAsync(() => connection.ConnectAsync(CancellationToken.None).DefaultTimeout());
            Assert.IsNotInstanceOf<TimeoutException>(x);
        }

        [Test]
        public async Task Server_Should_KeepRunning_After_FailedConnection()
        {
            var server = new LightweightRpcServer();
            try                               
            {
                server.AddEndPoint(new TcpRpcEndPoint("127.0.0.1", 50052, false, new SslServerOptions(TestCertificates.ServerCertificate) ));
                server.Start();

                await using (var failConnection = new TcpRpcConnection(new RpcConnectionInfo(new Uri("lightweight.tcp://localhost:50052")), new NegotiateClientOptions()))
                {
                    try
                    {
                        await failConnection.ConnectAsync(CancellationToken.None).DefaultTimeout();
                        Assert.Fail();
                    }
                    catch (Exception x) when (!(x is TimeoutException)) { }
                }

                var connection = new TcpRpcConnection(new RpcConnectionInfo(new Uri("lightweight.tcp://localhost:50052")), TestCertificates.SslClientOptions);
                try
                {
                    Assert.DoesNotThrowAsync(() => connection.ConnectAsync(CancellationToken.None).DefaultTimeout());
                    Assert.IsTrue(connection.IsConnected);
                    Assert.IsTrue(connection.IsEncrypted);
                }
                finally
                {
                    await connection.DisposeAsync().DefaultTimeout();
                }
            }
            finally
            {
                await server.DisposeAsync().DefaultTimeout();
            }
        }

        private static LightweightRpcServer CreateServer()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddRpcContextAccessor();

            serviceCollection.AddTransient<INegotiateService, NegotiateServiceImpl>();

            var services = serviceCollection.BuildServiceProvider();
            var server = new LightweightRpcServer(services);

            server.AddEndPoint(new TcpRpcEndPoint("127.0.0.1", 50052, false, new NegotiateServerOptions() ));

            return server;
        }

        public class NegotiateServiceImpl : INegotiateService
        {
            private readonly IRpcContextAccessor contextAccessor;

            public NegotiateServiceImpl(IRpcContextAccessor contextAccessor)
            {
                this.contextAccessor = contextAccessor;
            }

            public async Task<string> GetAsyncIdentityAsync()
            {
                var context = this.contextAccessor.RpcContext;
                await Task.Yield();
                return context.User?.Identity.Name;
            }

            public string GetIdentity()
            {
                var context = this.contextAccessor.RpcContext;
                return context.User?.Identity.Name;
            }

            public async Task ThrowAsyncIdentity()
            {
                var context = this.contextAccessor.RpcContext;
                await Task.Yield();
                throw new RpcFaultException(context.User?.Identity.Name ?? "", "Identity exception");
            }

            public void ThrowIdentity()
            {
                var context = this.contextAccessor.RpcContext;
                throw new RpcFaultException(context.User?.Identity.Name ?? "", "Identity exception");
            }
        }
    }
}