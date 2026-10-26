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

namespace SciTech.Rpc.Tests
{
    [RpcService]
    public interface IClientStreamingService
    {
        Task<int> SumValues(IAsyncEnumerable<SequenceData> values);

        Task<int> SumValuesWithSeed(int seed, IAsyncEnumerable<SequenceData> values);

        Task CollectValues(IAsyncEnumerable<SequenceData> values);

        Task<int> SumCancellableValues(IAsyncEnumerable<SequenceData> values, CancellationToken cancellationToken);

        Task<int> SumFirstValues(int count, IAsyncEnumerable<SequenceData> values);

        Task<int> SumValuesWithDelay(int delayMs, IAsyncEnumerable<SequenceData> values);

        Task<int[]> CollectInts(IAsyncEnumerable<int> values);
    }

    public class ClientStreamingServiceImpl : IClientStreamingService
    {
        private readonly List<int> collectedValues = new List<int>();

        private readonly TaskCompletionSource<bool> cancelledTcs = new TaskCompletionSource<bool>();

        private readonly TaskCompletionSource<bool> firstItemTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        private volatile bool isCancellableCallStarted;

        /// <summary>
        /// Gets a value indicating whether <see cref="SumCancellableValues"/> has been invoked.
        /// </summary>
        internal bool IsCancellableCallStarted => this.isCancellableCallStarted;

        /// <summary>
        /// Gets a task that completes when <see cref="SumCancellableValues"/> has received its first item.
        /// </summary>
        internal Task GetCancellableCallFirstItemAsync() => this.firstItemTcs.Task;

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
            this.isCancellableCallStarted = true;

            int sum = 0;
            try
            {
                await foreach (var value in values.WithCancellation(cancellationToken))
                {
                    this.firstItemTcs.TrySetResult(true);
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

        public async Task<int> SumFirstValues(int count, IAsyncEnumerable<SequenceData> values)
        {
            // Returns as soon as 'count' items have been received, without
            // enumerating the rest of the uploaded stream.
            int sum = 0;
            int received = 0;
            await foreach (var value in values)
            {
                sum += value.SequenceNo;
                received++;
                if (received >= count)
                {
                    break;
                }
            }

            return sum;
        }

        public async Task<int> SumValuesWithDelay(int delayMs, IAsyncEnumerable<SequenceData> values)
        {
            int sum = 0;
            await foreach (var value in values)
            {
                await Task.Delay(delayMs);
                sum += value.SequenceNo;
            }

            return sum;
        }

        public async Task<int[]> CollectInts(IAsyncEnumerable<int> values)
        {
            var collected = new List<int>();
            await foreach (var value in values)
            {
                collected.Add(value);
            }

            return collected.ToArray();
        }
    }

    [TestFixture(RpcConnectionType.LightweightTcp)]
    [TestFixture(RpcConnectionType.LightweightInproc)]
#if NET
    [TestFixture(RpcConnectionType.NetGrpc)]
#endif
    public class ClientStreamingTests : ClientServerTestsBase
    {
        public ClientStreamingTests(RpcConnectionType connectionType)
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

        private static async IAsyncEnumerable<SequenceData> GenerateSlowSequence(int count, int delayMs, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (int i = 0; i < count; i++)
            {
                await Task.Delay(delayMs, cancellationToken);
                yield return new SequenceData(i + 1);
            }
        }

        private static async IAsyncEnumerable<SequenceData> GenerateFailingSequence(int count)
        {
            for (int i = 0; i < count; i++)
            {
                await Task.Yield();
                yield return new SequenceData(i + 1);
            }

            throw new InvalidOperationException("Upload failed.");
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
        public async Task ClientStreaming_ValueTypeItems_IncludingDefaults_AreReceived()
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

                // Protobuf doesn't write default values, so an item 0 is an empty message. The position of a message, not its
                // content, tells whether it's the request or an item.
                var values = new[] { 0, 1, 0, -2, 0 };
                var result = await clientStreamingService.CollectInts(ToAsyncEnumerable(values)).DefaultTimeout();

                CollectionAssert.AreEqual(values, result);
            }
            finally
            {
                await server.ShutdownAsync();
            }

            static async IAsyncEnumerable<int> ToAsyncEnumerable(int[] values)
            {
                foreach (var value in values)
                {
                    await Task.Yield();
                    yield return value;
                }
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

        [Test]
        public async Task ClientStreaming_CancelledCall_ThrowsAndCancelsServer()
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

                using var cts = new CancellationTokenSource();

                // The slow sequence honors the cancellation token, so both the upload
                // loop and the server-side handler observe the cancellation.
                var callTask = clientStreamingService.SumCancellableValues(GenerateSlowSequence(1000, 20, cts.Token), cts.Token);

                // Cancel the call while the (slow) upload stream is still in progress, once the server has received the
                // call. A fixed delay is not enough, since e.g. establishing a gRPC connection may take longer.
                await serviceImpl.GetCancellableCallFirstItemAsync().DefaultTimeout();
                cts.Cancel();

                Assert.CatchAsync<OperationCanceledException>(async () => await callTask.DefaultTimeout());

                // The server-side handler should observe the cancellation.
                Assert.IsTrue(await serviceImpl.GetIsCancelledAsync().DefaultTimeout());
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }

        [Test]
        public async Task ClientStreaming_FailedUpload_ThrowsAndCancelsServer()
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

                var exception = Assert.ThrowsAsync<RpcFailureException>(async () =>
                {
                    await clientStreamingService.SumCancellableValues(GenerateFailingSequence(3), CancellationToken.None).DefaultTimeout();
                });
                Assert.IsInstanceOf<InvalidOperationException>(exception.InnerException);

                // The call is not cancellable, but the server-side handler must not be left waiting
                // for the rest of the stream.
                var cancelledTask = serviceImpl.GetIsCancelledAsync();
                if (this.ConnectionType == RpcConnectionType.NetGrpc
                    && await Task.WhenAny(cancelledTask, Task.Delay(TimeSpan.FromSeconds(1))) != cancelledTask)
                {
                    // The client resets the gRPC call when the upload fails, which discards the request messages that the
                    // server hasn't read yet. If the upload fails quickly, the handler may not have been invoked at all,
                    // and then there's nothing to cancel. (The Lightweight request frame is always delivered.)
                    Assert.IsFalse(serviceImpl.IsCancellableCallStarted, "The handler was invoked, but not cancelled.");
                }
                else
                {
                    Assert.IsTrue(await cancelledTask.DefaultTimeout());
                }
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }

        [Test]
        public async Task ClientStreaming_HandlerReturnsBeforeStreamEnd_ReturnsResult()
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

                // The handler returns after reading the first 3 items. The remaining items
                // (including more than the bounded channel capacity) must not cause the call
                // to hang, since the server stops reading the stream early.
                int result = await clientStreamingService.SumFirstValues(3, GenerateSequence(100)).DefaultTimeout();

                // 1 + 2 + 3 = 6
                Assert.AreEqual(6, result);
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }

        [Test]
        public async Task ClientStreaming_Timeout_ThrowsTimeoutException()
        {
            var builder = new RpcServiceDefinitionsBuilder();
            builder.RegisterService<IClientStreamingService>();

            var (server, connection) = this.CreateServerAndConnection(
                builder,
                configClientOptions: o => o.StreamingCallTimeout = TimeSpan.FromMilliseconds(200));

            var serviceImpl = new ClientStreamingServiceImpl();
            using var publishedService = server.ServicePublisher.PublishSingleton<IClientStreamingService>(serviceImpl);
            try
            {
                server.Start();

                var clientStreamingService = connection.GetServiceSingleton<IClientStreamingService>();

                // The server delays processing of each item, so the overall call cannot
                // complete within the configured streaming call timeout.
                Assert.ThrowsAsync<TimeoutException>(async () =>
                {
                    await clientStreamingService.SumValuesWithDelay(500, GenerateSlowSequence(20, 50)).DefaultTimeout();
                });
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }
    }
}
