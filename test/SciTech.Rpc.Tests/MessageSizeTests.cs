using NUnit.Framework;
using SciTech.Rpc.Client;
using SciTech.Rpc.Server;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests
{
    public abstract class MessageSizeTests : ClientServerTestsBase
    {
        protected readonly bool KeepConnectionAlive;

        protected MessageSizeTests(IRpcSerializer serializer, RpcConnectionType connectionType, bool keepConnectionAlive = true) :
            base(serializer, connectionType)
        {
            this.KeepConnectionAlive = keepConnectionAlive;
        }

        protected enum ExpectedRpcSizeException
        {
            None,
            Communication,
            Failure
        }

        [Test]
        public Task LargeRequestMessage_ExactSize_Test()
        {
            // Only correct for lightweight, since gRPC doesn't include the header 
            // size. gRPC tests will pass anyway, since payload is smaller. And 
            // this test is mainly intended for lightweight
            const int ExpectedLargeRequestFrameSize = 4000057;

            return this.TestLargeRequest(
                ExpectedRpcSizeException.None,
                clientConfig: o =>
                {
                    o.SendMaxMessageSize = ExpectedLargeRequestFrameSize;
                    o.ReceiveMaxMessageSize = 1000;
                },
                serverConfig: o =>
                {
                    o.ReceiveMaxMessageSize = ExpectedLargeRequestFrameSize;
                    o.SendMaxMessageSize = 1000;
                });
        }

        [Test]
        public Task LargeResponseMessage_ExactSize_Test()
        {
            // Only correct for lightweight, since gRPC doesn't include the header 
            // size. gRPC tests will pass anyway, since payload is smaller. And 
            // this test is mainly intended for lightweight
            const int ExpectedLargeResponseFrameSize = 3983548;

            return this.TestLargeResponse(
                ExpectedRpcSizeException.None,
                1000000,
                clientConfig: o =>
                {
                    o.ReceiveMaxMessageSize = ExpectedLargeResponseFrameSize;
                    o.SendMaxMessageSize = 1000; // Just a low number, should be enough
                },
                serverConfig: o =>
                {
                    o.SendMaxMessageSize = ExpectedLargeResponseFrameSize;
                    o.ReceiveMaxMessageSize = 1000; // Just a low number, should be enough
                });
        }

        [Test]
        public Task TooLargeClientReceiveMessage_Should_Throw()
        {
            return this.TestLargeResponse(
                this.KeepConnectionAlive ? ExpectedRpcSizeException.Failure : ExpectedRpcSizeException.Communication,
                10000,
                clientConfig: o =>
                {
                    o.ReceiveMaxMessageSize = 10000;
                });
        }

        [Test]
        public Task TooLargeClientSendMessage_Should_Throw()
        {
            return this.TestLargeRequest(
                ExpectedRpcSizeException.Failure,
                clientConfig: o =>
                {
                    o.SendMaxMessageSize = 10000;
                });
        }

        [Test]
        public Task TooLargeServerReceiveMessage_Should_Throw()
        {
            return this.TestLargeRequest(
                this.KeepConnectionAlive ? ExpectedRpcSizeException.Failure : ExpectedRpcSizeException.Communication,
                serverConfig: o =>
                {
                    o.ReceiveMaxMessageSize = 10000;
                });
        }

        [Test]
        public Task TooLargeServerSendMessage_Should_Throw()
        {
            return this.TestLargeResponse(
                ExpectedRpcSizeException.Failure,
                10000,
                serverConfig: o =>
                {
                    o.SendMaxMessageSize = 10000;
                });
        }

        protected async Task TestLargeRequest(
            ExpectedRpcSizeException expectedException,
            Action<RpcServerOptions> serverConfig = null,
            Action<RpcClientOptions> clientConfig = null)
        {
            var definitionBuilder = new RpcServiceDefinitionBuilder();
            definitionBuilder.RegisterService<ISimpleService>();

            var (host, connection) = this.CreateServerAndConnection(definitionBuilder,
                configServerOptions: serverConfig,
                configClientOptions: clientConfig);

            var servicePublisher = host.ServicePublisher;
            using (servicePublisher.PublishSingleton<ISimpleService>(new TestSimpleServiceImpl()))
            {
                var serviceClient = connection.GetServiceSingleton<ISimpleService>();
                host.Start();
                try
                {
                    Task<int> RequestFunc() => serviceClient.SumAsync(new int[2000000]).DefaultTimeout();
                    switch (expectedException)
                    {
                        case ExpectedRpcSizeException.Communication:
                            Assert.ThrowsAsync<RpcCommunicationException>(() => RequestFunc());
                            break;
                        case ExpectedRpcSizeException.Failure:
                            var e = Assert.ThrowsAsync<RpcFailureException>(() => RequestFunc());
                            Assert.AreEqual(RpcFailure.SizeLimitExceeded, e.Failure);
                            break;
                        case ExpectedRpcSizeException.None:
                            Assert.AreEqual(0, await RequestFunc());
                            break;
                    }

                    Assert.AreEqual(5, await serviceClient.SumAsync(new int[] { 2, 3 }).DefaultTimeout());
                }
                finally
                {
                    await host.ShutdownAsync().DefaultTimeout();
                }
            }
        }

        protected async Task TestLargeResponse(
            ExpectedRpcSizeException expectedException,
            int sizeParameter,
            Action<RpcServerOptions> serverConfig = null,
            Action<RpcClientOptions> clientConfig = null)
        {
            var definitionBuilder = new RpcServiceDefinitionBuilder();
            definitionBuilder.RegisterService<ISimpleService>();

            var (host, connection) = this.CreateServerAndConnection(definitionBuilder,
                configServerOptions: serverConfig,
                configClientOptions: clientConfig);

            var servicePublisher = host.ServicePublisher;
            using (servicePublisher.PublishSingleton<ISimpleService>(new TestSimpleServiceImpl()))
            {
                var serviceClient = connection.GetServiceSingleton<ISimpleService>();
                host.Start();
                try
                {
                    Task<int[]> RequestFunc() => serviceClient.GetArrayAsync(sizeParameter).DefaultTimeout();
                    switch (expectedException)
                    {
                        case ExpectedRpcSizeException.Communication:
                            Assert.ThrowsAsync<RpcCommunicationException>(() => RequestFunc());
                            break;
                        case ExpectedRpcSizeException.Failure:
                            var e = Assert.ThrowsAsync<RpcFailureException>(() => RequestFunc());
                            Assert.AreEqual(RpcFailure.SizeLimitExceeded, e.Failure);
                            break;
                        case ExpectedRpcSizeException.None:
                            Assert.AreEqual(sizeParameter, (await RequestFunc()).Length);
                            break;
                    }


                    Assert.AreEqual(5, (await serviceClient.GetArrayAsync(5).DefaultTimeout()).Length);
                }
                finally
                {
                    await host.ShutdownAsync().DefaultTimeout();
                }
            }
        }
    }
}
