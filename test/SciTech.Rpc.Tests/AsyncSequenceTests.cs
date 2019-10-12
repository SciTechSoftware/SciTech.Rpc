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
        IAsyncEnumerable<SequenceData> GetSequenceAsEnumerable(int count, TimeSpan delay, int delayFrequency);
        
        IAsyncEnumerable<SequenceData> GetSequenceAsCancellableEnumerable(int count, TimeSpan delay, int delayFrequency, CancellationToken cancellationToken);
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

                DateTime start = DateTime.UtcNow;
                int lastNo = 0;

                var exceptionExpression = Is.InstanceOf<TimeoutException>();
                if( this.ConnectionType == RpcConnectionType.NetGrpc)
                {
                    // It seems like NetGrpc throws OperationCancelledException instead of DeadlineExceeded RpcException (but
                    // it also seems a bit timing dependent). Let's allow both exceptions.
                    exceptionExpression = exceptionExpression.Or.InstanceOf<OperationCanceledException>();
                }
                
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
                        await foreach (var sequenceData in sequenceService.GetSequenceAsCancellableEnumerable(1000,  TimeSpan.FromMilliseconds(20), 10, cts.Token))
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


        public async IAsyncEnumerable<SequenceData> GetSequenceAsEnumerable(int count, TimeSpan delay, int delayFrequency)
        {
            for (int i = 0; i < count; i++)
            {
                if( delayFrequency != 0 && i % delayFrequency == 0 )
                {
                    await Task.Delay(delay);
                } else
                {
                    await Task.Yield();
                }

                yield return new SequenceData(i + 1);
            }
        }

        public async IAsyncEnumerable<SequenceData> GetSequenceAsCancellableEnumerable(int count, TimeSpan delay, int delayFrequency, [EnumeratorCancellation]CancellationToken cancellationToken)
        {
            for (int i = 0; i < count; i++)
            {
                try
                {
                    if (delayFrequency != 0 && i % delayFrequency == 0)
                    {
                        await Task.Delay(delay);
                    }
                    else
                    {
                        await Task.Yield();
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    this.cancelledTcs.TrySetResult(cancellationToken.IsCancellationRequested);
                    throw;
                }

                yield return new SequenceData(i + 1); 
            }
            
            this.cancelledTcs.TrySetResult(cancellationToken.IsCancellationRequested);
        }
    }
}
