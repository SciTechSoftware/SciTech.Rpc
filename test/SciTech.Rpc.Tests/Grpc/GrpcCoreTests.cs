using NUnit.Framework;
using SciTech.Rpc.Grpc.Server;
using SciTech.Rpc.Tests;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.Grpc.Tests
{
    public class GrpcCoreFullStackTestsBase
    {
        internal const int GrpcTestPort = 15959;

        private protected readonly Dictionary<int, CallsCounter> activeCallCountersDictionary = new Dictionary<int, CallsCounter>();

        [TearDown]
        public void Cleanup()
        {
            this.activeCallCountersDictionary.Clear();
        }

        public static RpcServerConnectionInfo CreateConnectionInfo()
        {
            return new RpcServerConnectionInfo(new Uri($"grpc://localhost:{GrpcTestPort}"));
        }

        public static GrpcServerEndPoint CreateEndPoint()
        {
            var certificatePair = new GrpcCore.KeyCertificatePair(
                File.ReadAllText(Path.Combine(TestCertificates.ServerCertDir, "server.crt")),
                File.ReadAllText(Path.Combine(TestCertificates.ServerCertDir, "server.key")));
            var credentials = new GrpcCore.SslServerCredentials(new GrpcCore.KeyCertificatePair[] { certificatePair });

            return new GrpcServerEndPoint("localhost", GrpcTestPort, false, credentials);
        }


        internal class CallsCounter
        {
            internal bool firstCallee = true;

            internal int nActiveCalls;

            internal int nTotalCalls;

            internal Task tasksQueueTask;
        }
    }

    [TestFixture]
    public class GrpcCoreTests : GrpcCoreFullStackTestsBase
    {
        [Test]
        public async Task ManyStreamingServerCallsManyClientsTest()
        {
            const int TotalCallsPerClient = 100;
            const int TotalClients = 100;

            GrpcCore.Server server = this.StartSimpleServiceServer();

            List<Task> activeCalls = new List<Task>();
            TaskCompletionSource<bool> tasksQueuedCompletionSource = new TaskCompletionSource<bool>();
            for (int clientIndex = 0; clientIndex < TotalClients; clientIndex++)
            {
                GrpcCore.Channel channel = new GrpcCore.Channel($"127.0.0.1:{GrpcTestPort}", GrpcCore.ChannelCredentials.Insecure);
                var client = new SimpleCoreService.SimpleCoreServiceClient(channel);


                var callsCounter = new CallsCounter { nTotalCalls = TotalCallsPerClient, tasksQueueTask = tasksQueuedCompletionSource.Task };

                int clientId = clientIndex;
                this.activeCallCountersDictionary.Add(clientId, callsCounter);

                for (int i = 0; i < 100; i++)
                {
                    activeCalls.Add(MakeStreamingServerCalls(client, clientId, 10, 10));
                    lock (callsCounter)
                    {
                        callsCounter.nActiveCalls++;
                    }
                }
            }

            foreach (var callsCounter in this.activeCallCountersDictionary.Values)
            {
                Assert.AreEqual(TotalCallsPerClient, callsCounter.nActiveCalls);
            }

            tasksQueuedCompletionSource.SetResult(true);

            await Task.WhenAll(activeCalls);

            foreach (var callsCounter in this.activeCallCountersDictionary.Values)
            {
                Assert.AreEqual(0, callsCounter.nActiveCalls);
            }

            await server.ShutdownAsync();
        }

        [Test]
        public async Task ManyStreamingServerCallsSingleClientTest()
        {
            GrpcCore.Server server = this.StartSimpleServiceServer();

            GrpcCore.Channel channel = new GrpcCore.Channel($"127.0.0.1:{GrpcTestPort}", GrpcCore.ChannelCredentials.Insecure);
            var client = new SimpleCoreService.SimpleCoreServiceClient(channel);

            List<Task> activeCalls = new List<Task>();
            const int TotalClientCalls = 100;
            const int ClientId = 0;
            TaskCompletionSource<bool> tasksQueuedCompletionSource = new TaskCompletionSource<bool>();

            var callsCounter = new CallsCounter { nTotalCalls = TotalClientCalls, tasksQueueTask = tasksQueuedCompletionSource.Task };

            this.activeCallCountersDictionary.Add(ClientId, callsCounter);

            for (int i = 0; i < 100; i++)
            {
                activeCalls.Add(MakeStreamingServerCalls(client, ClientId, 10, 10));
                lock (callsCounter)
                {
                    callsCounter.nActiveCalls++;
                }
            }
            Assert.AreEqual(TotalClientCalls, callsCounter.nActiveCalls);
            tasksQueuedCompletionSource.SetResult(true);

            await Task.WhenAll(activeCalls);
            Assert.AreEqual(0, callsCounter.nActiveCalls);

            await server.ShutdownAsync();
        }

        private static async Task MakeStreamingServerCalls(SimpleCoreService.SimpleCoreServiceClient client, int clientId, int initialValue, int nResponses)
        {
            using (var streamingCall = client.ServerStreamTest(new StreamRequest { ClientId = clientId, StartValue = 10 }))
            {
                int expectedValue = 10;
                while (await streamingCall.ResponseStream.MoveNext(CancellationToken.None))
                {
                    Assert.AreEqual(expectedValue, streamingCall.ResponseStream.Current.Value);
                    expectedValue++;
                }

                Assert.AreEqual(expectedValue, initialValue + nResponses);

            }
        }

        private GrpcCore.Server StartSimpleServiceServer()
        {
            GrpcCore.Server server = new GrpcCore.Server
            {
                Services = { SimpleCoreService.BindService(new SimpleCoreServiceImpl(this.activeCallCountersDictionary)) },
                Ports = { new GrpcCore.ServerPort("localhost", GrpcTestPort, GrpcCore.ServerCredentials.Insecure) }
            };
            server.Start();
            return server;
        }
    }

    internal class SimpleCoreServiceImpl : SimpleCoreService.SimpleCoreServiceBase
    {
        private readonly Dictionary<int, GrpcCoreTests.CallsCounter> activeCallCountersDictionary;

        internal SimpleCoreServiceImpl(Dictionary<int, GrpcCoreTests.CallsCounter> activeCallCountersDictionary)
        {
            this.activeCallCountersDictionary = activeCallCountersDictionary;
        }

        public override async Task ServerStreamTest(StreamRequest request, GrpcCore.IServerStreamWriter<StreamResponse> responseStream, GrpcCore.ServerCallContext context)
        {
            int value = request.StartValue;

            await Task.Run(async () =>
            {
                var callsCounter = this.activeCallCountersDictionary[request.ClientId];
                await callsCounter.tasksQueueTask;

                lock (callsCounter)
                {
                    if (callsCounter.firstCallee)
                    {
                        Assert.AreEqual(callsCounter.nTotalCalls, callsCounter.nActiveCalls);
                        callsCounter.firstCallee = false;
                    }

                }

                for (int i = 0; i < 10; i++)
                {
                    await responseStream.WriteAsync(new StreamResponse { Value = value++ });
                    await Task.Delay(10, context.CancellationToken);
                }

                lock (callsCounter)
                {
                    callsCounter.nActiveCalls--;
                }
            });
        }
    }
}
