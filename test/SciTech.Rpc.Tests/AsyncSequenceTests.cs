using NUnit.Framework;
using SciTech.Collections;
using SciTech.Rpc.Server;
using SciTech.Rpc.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.CompilerServices;
using SciTech.Rpc.Serialization;

namespace SciTech.Rpc.Tests
{

    [RpcService]
    public interface ISequenceService
    {
        IAsyncEnumerable<SequenceData> GetSequenceAsEnumerable(int count, TimeSpan delay, int delayFrequency,bool initialDelay = true);
        
        IAsyncEnumerable<SequenceData> GetSequenceAsCancellableEnumerable(int count, TimeSpan delay, int delayFrequency, bool initialDelay, CancellationToken cancellationToken);

        void SimpleCall();
    }

    public abstract class AsyncSequenceTests : ClientServerTestsBase
    {

        protected AsyncSequenceTests(IRpcSerializer serializer, RpcConnectionType connectionType, bool roundTripCancellation) : base(serializer, connectionType)
        {
            this.RoundTripCancellation = roundTripCancellation;

        }

        public bool RoundTripCancellation { get; }


        [Test]
        public async Task SequenceEnumerableTest()
        {
            var builder = new RpcServiceDefinitionsBuilder();
            builder.RegisterService<ISequenceService>();

            var (server, connection) = this.CreateServerAndConnection(builder);
            var sequenceServiceImpl = new SequenceServiceImpl();
            using (var publishedService = server.ServicePublisher.PublishSingleton<ISequenceService>(sequenceServiceImpl))
            {
                try
                {
                    server.Start();

                    var sequenceService = connection.GetServiceSingleton<ISequenceService>();

                    int lastNo = 0;
                    await foreach( var sequenceData in sequenceService.GetSequenceAsEnumerable(5, TimeSpan.FromMilliseconds(0), 0))
                    {
                        Assert.AreEqual(lastNo + 1, sequenceData.SequenceNo);
                        lastNo = sequenceData.SequenceNo;
                    }
                    Assert.AreEqual(5, lastNo);
                }
                finally
                {
                    await server.ShutdownAsync();
                }
            }
        }

        [Test]
        public async Task SequenceEnumerableTimeoutTest()
        {
            var builder = new RpcServiceDefinitionsBuilder();
            builder.RegisterService<ISequenceService>();

            var (server, connection) = this.CreateServerAndConnection(builder,
                null,
                o=> {
                    o.StreamingCallTimeout = TimeSpan.FromSeconds(1);
                });

            var sequenceServiceImpl = new SequenceServiceImpl();
            using var publishedService = server.ServicePublisher.PublishSingleton<ISequenceService>(sequenceServiceImpl);
            try
            {
                server.Start();

                var sequenceService = connection.GetServiceSingleton<ISequenceService>();


                var exceptionExpression = Is.InstanceOf<TimeoutException>();
                if( this.ConnectionType == RpcConnectionType.NetGrpc)
                {
                    // It seems like NetGrpc throws OperationCancelledException instead of DeadlineExceeded RpcException (but
                    // it also seems a bit timing dependent). Let's allow both exceptions.
                    exceptionExpression = exceptionExpression.Or.InstanceOf<OperationCanceledException>();
                }

                DateTime connectStart = DateTime.UtcNow;
                // Force connection
//                sequenceService.SimpleCall();

                var connectDuration = DateTime.UtcNow - connectStart;
                Console.WriteLine("Connect duration: " + connectDuration);

                DateTime start = DateTime.UtcNow;
                int lastNo = 0;

                Assert.ThrowsAsync(exceptionExpression, async () =>
                {
                    async Task GetSequence()
                    {
                        await foreach (var sequenceData in sequenceService.GetSequenceAsEnumerable(2000, TimeSpan.FromMilliseconds(1), 1))
                        {
                            Assert.AreEqual(lastNo + 1, sequenceData.SequenceNo);
                            lastNo = sequenceData.SequenceNo;
                        }
                    }

                    await GetSequence().DefaultTimeout();
                });

                TimeSpan duration = DateTime.UtcNow - start;
                Assert.GreaterOrEqual(duration, TimeSpan.FromSeconds(1));
                Assert.Less(duration, TimeSpan.FromSeconds(2));
                Assert.Greater(lastNo, 10);
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }

        [Test]
        public async Task SequenceEnumerableWithCancellationTest()
        {
            var builder = new RpcServiceDefinitionsBuilder();
            builder.RegisterService<ISequenceService>();

            var (server, connection) = this.CreateServerAndConnection(builder);
            var sequenceServiceImpl = new SequenceServiceImpl();
            using (var publishedService = server.ServicePublisher.PublishSingleton<ISequenceService>(sequenceServiceImpl))
            {
                try
                {
                    server.Start();

                    var sequenceService = connection.GetServiceSingleton<ISequenceService>();

                    int lastNo = 0;
                    CancellationTokenSource cts = new CancellationTokenSource();

                    Assert.CatchAsync<OperationCanceledException>( async ()=>
                    {
                        await foreach (var sequenceData in sequenceService.GetSequenceAsCancellableEnumerable(1000,  TimeSpan.FromMilliseconds(20), 10, true, cts.Token))
                        {
                            Assert.AreEqual(lastNo + 1, sequenceData.SequenceNo);
                            lastNo = sequenceData.SequenceNo;
                            if (lastNo == 3)
                            {
                                cts.Cancel();
                            }
                        }
                    });

                    if (this.RoundTripCancellation)
                    {
                        Assert.Less(lastNo, 100);
                        Assert.GreaterOrEqual(lastNo, 3);
                    }
                    else
                    {
                        Assert.AreEqual(3, lastNo);
                    }

                    //
                    // Assert.IsTrue(await sequenceServiceImpl.GetIsCancelledAsync().DefaultTimeout());
                }
                finally
                {
                    await server.ShutdownAsync();
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task StreamingCall_UnpublishedInstance_ShouldThrow(bool cancellable)
        {
            var (server, connection) = this.CreateServerAndConnection();

            var serviceImpl = new SequenceServiceImpl();
            var isCancelledTask = serviceImpl.GetIsCancelledAsync();

            var publishedInstance = server.PublishInstance<ISequenceService>(serviceImpl, true);
            server.Start();
            try
            {
                var client = connection.GetServiceInstance(publishedInstance.Value);

                IAsyncEnumerable<SequenceData> sequence;
                if (cancellable)
                {
                    sequence = client.GetSequenceAsCancellableEnumerable(2, TimeSpan.FromSeconds(10), 1, false, CancellationToken.None);
                }
                else
                {
                    sequence = client.GetSequenceAsEnumerable(2, TimeSpan.FromSeconds(10), 1, false);
                }

                var sequenceEnum = sequence.GetAsyncEnumerator();
                await sequenceEnum.MoveNextAsync();
                var first = sequenceEnum.Current;

                // Unpublish the server side service after the first data has been received
                publishedInstance.Dispose();

                Assert.NotNull(first);
                Assert.ThrowsAsync<RpcServiceUnavailableException>(async () =>
                {
                    await sequenceEnum.MoveNextAsync().AsTask().TimeoutAfter(TimeSpan.FromSeconds(2));
                }
                );

                if (cancellable)
                {
                    Assert.IsTrue(await isCancelledTask.DefaultTimeout());
                }
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task StreamingCall_UnpublishedAutoInstance_ShouldThrow(bool cancellable)
        {
            var definitionsBuilder = new RpcServiceDefinitionsBuilder();
            definitionsBuilder.RegisterService<ISequenceService>();
            var (server, connection) = this.CreateServerAndConnection(
                definitionsBuilder,
                configServerOptions: o=>
                {
                    o.AllowAutoPublish = true;
                });
            
            var providerImpl = new SequenceServiceProviderImpl();
            var isCancelledTask = ((SequenceServiceImpl)providerImpl.SequenceService).GetIsCancelledAsync();

            var publishedProvider = server.PublishInstance((ISequenceServiceProvider)providerImpl, true);
            server.Start();
            try
            {
                var providerClient = connection.GetServiceInstance(publishedProvider.Value);
                var sequenceClient = providerClient.SequenceService;

                IAsyncEnumerable<SequenceData> sequence;
                if (cancellable)
                {
                    sequence = sequenceClient.GetSequenceAsCancellableEnumerable(2, TimeSpan.FromSeconds(10), 1, false, CancellationToken.None);
                }
                else
                {
                    sequence = sequenceClient.GetSequenceAsEnumerable(2, TimeSpan.FromSeconds(10), 1, false);
                }

                var sequenceEnum = sequence.GetAsyncEnumerator();
                await sequenceEnum.MoveNextAsync();
                var first = sequenceEnum.Current;

                // Unpublish the server side service after the first data has been received
                providerImpl.ReleaseServices();
                // Make sure that the auto-published instance is GCed (and thus unpublished).
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                Assert.NotNull(first);

                Assert.ThrowsAsync<RpcServiceUnavailableException>(async () =>
                {
                    await sequenceEnum.MoveNextAsync().AsTask().DefaultTimeout();
                });

                if ( cancellable )
                {
                    Assert.IsTrue(await isCancelledTask.DefaultTimeout());
                }                
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }
    }

    [RpcService]
    public interface ISequenceServiceProvider
    {
        ISequenceService SequenceService { get; }
        
        ICallbackService CallbackService { get; }
    }

    public class SequenceServiceProviderImpl : ISequenceServiceProvider
    {
        internal WeakReference wrSequenceService;
        internal WeakReference wrCallbackService;

        public SequenceServiceProviderImpl()
        {
            this.SequenceService = new SequenceServiceImpl();
            this.CallbackService = new CallbackServiceImpl();
            wrSequenceService = new WeakReference(this.SequenceService);
            wrCallbackService = new WeakReference(this.CallbackService);
        }

        public ISequenceService SequenceService { get; private set; }
        
        public ICallbackService CallbackService { get; private set; }

        internal void ReleaseServices()
        {
            this.SequenceService = null;
            this.CallbackService = null;
        }
    }

    [DataContract]
    public class SequenceData
    {
        public SequenceData() { }

        public SequenceData(int sequenceNo)
        {
            this.SequenceNo = sequenceNo;
        }

        [DataMember(Order = 1)]
        public int SequenceNo { get; set; }
    }

    public class SequenceServiceImpl : ISequenceService
    {
        private TaskCompletionSource<bool> cancelledTcs = new TaskCompletionSource<bool>();

        internal Task<bool> GetIsCancelledAsync() => this.cancelledTcs.Task;


        public IAsyncEnumerable<SequenceData> GetSequenceAsEnumerable(int count, TimeSpan delay, int delayFrequency, bool initialDelay)
        {
            static async IAsyncEnumerable<SequenceData> StaticGetSequence(int count, TimeSpan delay, int delayFrequency, bool initialDelay)
            {
                for (int i = 0; i < count; i++)
                {
                    if (( i > 0 || initialDelay ) && delayFrequency != 0 && i % delayFrequency == 0)
                    {
                        await Task.Delay(delay);
                    }
                    else
                    {
                        await Task.Yield();
                    }

                    yield return new SequenceData(i + 1);
                }
            }

            return StaticGetSequence(count, delay, delayFrequency, initialDelay);
        }

        static async IAsyncEnumerable<SequenceData> StaticGetSequenceAsCancellableEnumerable(
            int count, TimeSpan delay, int delayFrequency, bool initialDelay, TaskCompletionSource<bool> cancelledTcs,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {

            for (int i = 0; i < count; i++)
            {
                try
                {
                    if ((i > 0 || initialDelay) && delayFrequency != 0 && i % delayFrequency == 0)
                    {
                        await Task.Delay(delay, cancellationToken);
                    }
                    else
                    {
                        await Task.Yield();
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    cancelledTcs.TrySetResult(cancellationToken.IsCancellationRequested);
                    throw;
                }

                yield return new SequenceData(i + 1);
            }

            cancelledTcs.TrySetResult(cancellationToken.IsCancellationRequested);
        }

        public IAsyncEnumerable<SequenceData> GetSequenceAsCancellableEnumerable(int count, TimeSpan delay, int delayFrequency, bool initialDelay, CancellationToken cancellationToken)
        {
            return StaticGetSequenceAsCancellableEnumerable(count, delay, delayFrequency, initialDelay, this.cancelledTcs, cancellationToken);
        }

        public void SimpleCall() 
        {
        }
    }
}
