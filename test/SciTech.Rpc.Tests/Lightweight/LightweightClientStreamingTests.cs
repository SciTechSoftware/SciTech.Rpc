using NUnit.Framework;
using SciTech.Rpc.Client;
using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Lightweight
{
    [RpcService]
    public interface IClientStreamingService
    {
        Task<int> SumValues(IAsyncEnumerable<SequenceData> values);

        Task<int> SumValuesWithSeed(int seed, IAsyncEnumerable<SequenceData> values);

        Task CollectValues(IAsyncEnumerable<SequenceData> values);

        Task<int> SumCancellableValues(IAsyncEnumerable<SequenceData> values, CancellationToken cancellationToken);
    }

    public class ClientStreamingServiceImpl : IClientStreamingService
    {
        private readonly List<int> collectedValues = new List<int>();

        private readonly TaskCompletionSource<bool> cancelledTcs = new TaskCompletionSource<bool>();

        internal IReadOnlyList<int> CollectedValues
        {
            get
            {
                lock (this.collectedValues)
                {
                    return this.collectedValues.ToArray();
                }
            }
        }

        internal Task<bool> GetIsCancelledAsync() => this.cancelledTcs.Task;

        public async Task<int> SumValues(IAsyncEnumerable<SequenceData> values)
        {
            int sum = 0;
            await foreach (var value in values)
            {
                sum += value.SequenceNo;
            }

            return sum;
        }

        public async Task<int> SumValuesWithSeed(int seed, IAsyncEnumerable<SequenceData> values)
        {
            int sum = seed;
            await foreach (var value in values)
            {
                sum += value.SequenceNo;
            }

            return sum;
        }

        public async Task CollectValues(IAsyncEnumerable<SequenceData> values)
        {
            await foreach (var value in values)
            {
                lock (this.collectedValues)
                {
                    this.collectedValues.Add(value.SequenceNo);
                }
            }
        }

        public async Task<int> SumCancellableValues(IAsyncEnumerable<SequenceData> values, CancellationToken cancellationToken)
        {
            int sum = 0;
            try
            {
                await foreach (var value in values.WithCancellation(cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    sum += value.SequenceNo;
                }
            }
            catch (OperationCanceledException)
            {
                this.cancelledTcs.TrySetResult(true);
                throw;
            }

            return sum;
        }
    }

    [TestFixture(RpcConnectionType.LightweightTcp)]
    [TestFixture(RpcConnectionType.LightweightInproc)]
    public class LightweightClientStreamingTests : ClientServerTestsBase
    {
        public LightweightClientStreamingTests(RpcConnectionType connectionType)
            : base(new ProtobufRpcSerializer(), connectionType)
        {
        }

        [SetUp]
        public void SetupTest()
        {
            RpcProxyOptions.RoundTripCancellationsAndTimeouts = false;
        }

        [TearDown]
        public void TearDownTest()
        {
            RpcProxyOptions.RoundTripCancellationsAndTimeouts = false;
        }

        private static async IAsyncEnumerable<SequenceData> GenerateSequence(int count, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return new SequenceData(i + 1);
            }
        }

        [Test]
        public async Task ClientStreaming_SumValues_ReturnsAggregatedResult()
        {
            var builder = new RpcServiceDefinitionsBuilder();
            builder.RegisterService<IClientStreamingService>();

            var (server, connection) = this.CreateServerAndConnection(builder);
            var serviceImpl = new ClientStreamingServiceImpl();
            using var publishedService = server.ServicePublisher.PublishSingleton<IClientStreamingService>(serviceImpl);
            try
            {
                server.Start();

                var clientStreamingService = connection.GetServiceSingleton<IClientStreamingService>();

                int result = await clientStreamingService.SumValues(GenerateSequence(5)).DefaultTimeout();

                // 1 + 2 + 3 + 4 + 5 = 15
                Assert.AreEqual(15, result);
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }

        [Test]
        public async Task ClientStreaming_SumValuesWithSeed_IncludesRequestArgument()
        {
            var builder = new RpcServiceDefinitionsBuilder();
            builder.RegisterService<IClientStreamingService>();

            var (server, connection) = this.CreateServerAndConnection(builder);
            var serviceImpl = new ClientStreamingServiceImpl();
            using var publishedService = server.ServicePublisher.PublishSingleton<IClientStreamingService>(serviceImpl);
            try
            {
                server.Start();

                var clientStreamingService = connection.GetServiceSingleton<IClientStreamingService>();

                int result = await clientStreamingService.SumValuesWithSeed(100, GenerateSequence(3)).DefaultTimeout();

                // 100 + 1 + 2 + 3 = 106
                Assert.AreEqual(106, result);
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }

        [Test]
        public async Task ClientStreaming_VoidReturn_ReceivesAllItems()
        {
            var builder = new RpcServiceDefinitionsBuilder();
            builder.RegisterService<IClientStreamingService>();

            var (server, connection) = this.CreateServerAndConnection(builder);
            var serviceImpl = new ClientStreamingServiceImpl();
            using var publishedService = server.ServicePublisher.PublishSingleton<IClientStreamingService>(serviceImpl);
            try
            {
                server.Start();

                var clientStreamingService = connection.GetServiceSingleton<IClientStreamingService>();

                await clientStreamingService.CollectValues(GenerateSequence(4)).DefaultTimeout();

                CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, serviceImpl.CollectedValues);
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }

        [Test]
        public async Task ClientStreaming_EmptyStream_ReturnsSeed()
        {
            var builder = new RpcServiceDefinitionsBuilder();
            builder.RegisterService<IClientStreamingService>();

            var (server, connection) = this.CreateServerAndConnection(builder);
            var serviceImpl = new ClientStreamingServiceImpl();
            using var publishedService = server.ServicePublisher.PublishSingleton<IClientStreamingService>(serviceImpl);
            try
            {
                server.Start();

                var clientStreamingService = connection.GetServiceSingleton<IClientStreamingService>();

                int result = await clientStreamingService.SumValues(GenerateSequence(0)).DefaultTimeout();

                Assert.AreEqual(0, result);
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }
    }
}
