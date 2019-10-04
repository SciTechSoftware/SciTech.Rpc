using NUnit.Framework;
using SciTech.Rpc.Client;
using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests
{
    [RpcService]
    public interface ICancellationTestService
    {
        Task<int> AddWithDelay2Async(int msDelay, int a, CancellationToken cancellationToken, int b);

        Task<int> AddWithDelayAsync(int msDelay, int a, int b, CancellationToken cancellationToken);

        int AddWithDelayNoServerCancellation(int msDelay, int a, int b);
    }

    [RpcService(ServerDefinitionType = typeof(ICancellationTestService))]
    public interface ICancellationTestServiceClient : ICancellationTestService
    {
        int AddWithDelay(int msDelay, int a, int b, CancellationToken cancellationToken);

        int AddWithDelay2(int msDelay, int a, CancellationToken cancellationToken, int b);

        Task<int> AddWithDelayNoServerCancellationAsync(int msDelay, int a, int b, CancellationToken cancellationToken);
    }

    public abstract class CancellationTests : ClientServerTestsBase
    {
        protected CancellationTests(RpcConnectionType connectionType) : base(new ProtobufRpcSerializer(), connectionType)
        {
        }


        [Test]
        public Task CancelledBlockingCall_Should_ThrowCancellationException()
        {
            return this.CancellationTest(client =>
            {
                Assert.Catch<OperationCanceledException>(() =>
                {
                    try
                    {
                        var cts = new CancellationTokenSource();
                        Task.Run(async () =>
                       {
                           await Task.Delay(200);
                           cts.Cancel();
                       });

                        client.AddWithDelay(1000, 5, 6, cts.Token);
                    }catch( Exception )
                    {
                        throw;
                    }
                });
            });
        }

        [Test]
        public Task CancelledCall_Should_ThrowCancellationException()
        {
            return this.CancellationTest(client =>
           {
               Assert.CatchAsync<OperationCanceledException>(async () =>
               {
                   var cts = new CancellationTokenSource();
                   var addTask = client.AddWithDelayAsync(1000, 5, 6, cts.Token);
                   await Task.Delay(100);
                   cts.Cancel();
                   await addTask;
               });
           });
        }

        [Test]
        public Task CancelledCall2_Should_ThrowCancellationException()
        {
            return this.CancellationTest(client =>
            {
                Assert.CatchAsync<OperationCanceledException>(async () =>
                {
                    var cts = new CancellationTokenSource();
                    var addTask = client.AddWithDelay2Async(1000, 5, cts.Token, 6);
                    await Task.Delay(100);
                    cts.Cancel();
                    await addTask;
                });
            });
        }

        [Test]
        public Task CancelledClientCall_Should_ThrowCancellationException()
        {
            return this.CancellationTest(client =>
            {
                Assert.CatchAsync<OperationCanceledException>(async () =>
                {
                    var cts = new CancellationTokenSource();
                    var addTask = client.AddWithDelayNoServerCancellationAsync(1000, 5, 6, cts.Token);
                    await Task.Delay(100);
                    cts.Cancel();
                    await addTask;
                });
            });
        }



        private async Task CancellationTest(Action<ICancellationTestServiceClient> testAction)
        {
            var definitionsBuilder = new RpcServiceDefinitionBuilder();
            var (server, connection) = this.CreateServerAndConnection(definitionsBuilder);
            definitionsBuilder.RegisterService<ICancellationTestService>();

            server.Start();
            try
            {
                using (var publishedService = server.ServicePublisher.PublishInstance<ICancellationTestService>(new CancellationTestService()))
                {
                    var client = connection.GetServiceInstance<ICancellationTestServiceClient>(publishedService.Value);

                    testAction(client);


                }
            }
            finally
            {
                RpcProxyOptions.RoundTripCancellationsAndTimeouts = false;

                await server.ShutdownAsync();
            }

        }

        private class CancellationTestService : ICancellationTestService
        {
            public async Task<int> AddWithDelay2Async(int msDelay, int a, CancellationToken cancellationToken, int b)
            {
                await Task.Delay(msDelay, cancellationToken);

                return a + b;
            }

            public async Task<int> AddWithDelayAsync(int msDelay, int a, int b, CancellationToken cancellationToken)
            {
                await Task.Delay(msDelay, cancellationToken);

                return a + b;
            }

            public int AddWithDelayNoServerCancellation(int msDelay, int a, int b)
            {
                Thread.Sleep(msDelay);

                return a + b;
            }
        }
    }
}
