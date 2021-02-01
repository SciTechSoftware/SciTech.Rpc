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
using SciTech.Rpc.Internal;

namespace SciTech.Rpc.Tests
{

    [RpcService]
    public interface ICallbackService
    {
        Task PerformActionCallbacksAsync(int value, int count, Action<CallbackData> callback);
        
        void PerformSyncActionCallbacks(int value, int count, Action<CallbackData> callback);

        // Task PerformSimpleDelegateCallbacksAsync(int value, int count, SimpleDelegate callback);
    }

    public delegate void SimpleDelegate(CallbackData data);

    public delegate void SimpleDelegate2(int value1, int value2);

    [DataContract]
    public class CallbackData
    {
        [DataMember(Order =1)]
        public int Value { get; set; }

    }

    public abstract class CallbackTests : ClientServerTestsBase
    {

        protected CallbackTests(IRpcSerializer serializer, RpcConnectionType connectionType, bool roundTripCancellation) : base(serializer, connectionType)
        {
            this.RoundTripCancellation = roundTripCancellation;

        }

        public bool RoundTripCancellation { get; }


        [Test]
        public async Task CallbackMethod_Should_HandleAction()
        {
            var builder = new RpcServiceDefinitionsBuilder();
            builder.RegisterService<ICallbackService>();

            var (server, connection) = this.CreateServerAndConnection(builder);
            var callbackServiceImpl = new CallbackServiceImpl();
            using (var publishedService = server.PublishSingleton<ICallbackService>(callbackServiceImpl))
            {
                try
                {
                    server.Start();

                    var callbackService = connection.GetServiceSingleton<ICallbackService>();

                    int callCount = 0;

                    await callbackService.PerformActionCallbacksAsync(2, 10, v =>
                    {
                        Assert.AreEqual(2 + callCount, v.Value);
                        callCount++;
                     });


                    Assert.AreEqual(callCount, 10);
                }
                finally
                {
                    await server.ShutdownAsync();
                }
            }
        }

        [Test]
        public async Task SyncCallbackMethod_Should_HandleAction()
        {
            var builder = new RpcServiceDefinitionsBuilder();
            builder.RegisterService<ICallbackService>();

            var (server, connection) = this.CreateServerAndConnection(builder);
            var callbackServiceImpl = new CallbackServiceImpl();
            using (var publishedService = server.PublishSingleton<ICallbackService>(callbackServiceImpl))
            {
                try
                {
                    server.Start();

                    var callbackService = connection.GetServiceSingleton<ICallbackService>();

                    int callCount = 0;

                    callbackService.PerformSyncActionCallbacks(2, 10, v =>
                    {
                        Assert.AreEqual(2 + callCount, v.Value);
                        callCount++;
                    });


                    Assert.AreEqual(callCount, 10);
                }
                finally
                {
                    await server.ShutdownAsync();
                }
            }
        }
        //[Test]
        //public async Task CallbackMethod_Should_HandleOtherDelegate()
        //{
        //    var builder = new RpcServiceDefinitionsBuilder();
        //    builder.RegisterService<ICallbackService>();

        //    var (server, connection) = this.CreateServerAndConnection(builder);
        //    var callbackServiceImpl = new CallbackServiceImpl();
        //    using (var publishedService = server.PublishSingleton<ICallbackService>(callbackServiceImpl))
        //    {
        //        try
        //        {
        //            server.Start();

        //            var callbackService = connection.GetServiceSingleton<ICallbackService>();

        //            int callCount = 0;

        //            await callbackService.PerformActionCallbacksAsync(2, 10, v =>
        //            {
        //                Assert.AreEqual(2 + callCount, v.Value);
        //                callCount++;
        //            });


        //            Assert.AreEqual(callCount, 10);
        //        }
        //        finally
        //        {
        //            await server.ShutdownAsync();
        //        }
        //    }
        //}


        //        [Test]
        //        public async Task SequenceEnumerableTimeoutTest()
        //        {
        //            var builder = new RpcServiceDefinitionsBuilder();
        //            builder.RegisterService<ISequenceService>();

        //            var (server, connection) = this.CreateServerAndConnection(builder,
        //                null,
        //                o=> {
        //                    o.StreamingCallTimeout = TimeSpan.FromSeconds(1);
        //                });

        //            var sequenceServiceImpl = new SequenceServiceImpl();
        //            using var publishedService = server.ServicePublisher.PublishSingleton<ISequenceService>(sequenceServiceImpl);
        //            try
        //            {
        //                server.Start();

        //                var sequenceService = connection.GetServiceSingleton<ISequenceService>();


        //                var exceptionExpression = Is.InstanceOf<TimeoutException>();
        //                if( this.ConnectionType == RpcConnectionType.NetGrpc)
        //                {
        //                    // It seems like NetGrpc throws OperationCancelledException instead of DeadlineExceeded RpcException (but
        //                    // it also seems a bit timing dependent). Let's allow both exceptions.
        //                    exceptionExpression = exceptionExpression.Or.InstanceOf<OperationCanceledException>();
        //                }

        //                DateTime connectStart = DateTime.UtcNow;
        //                // Force connection
        ////                sequenceService.SimpleCall();

        //                var connectDuration = DateTime.UtcNow - connectStart;
        //                Console.WriteLine("Connect duration: " + connectDuration);

        //                DateTime start = DateTime.UtcNow;
        //                int lastNo = 0;

        //                Assert.ThrowsAsync(exceptionExpression, async () =>
        //                {
        //                    async Task GetSequence()
        //                    {
        //                        await foreach (var sequenceData in sequenceService.GetSequenceAsEnumerable(2000, TimeSpan.FromMilliseconds(1), 1))
        //                        {
        //                            Assert.AreEqual(lastNo + 1, sequenceData.SequenceNo);
        //                            lastNo = sequenceData.SequenceNo;
        //                        }
        //                    }

        //                    await GetSequence().DefaultTimeout();
        //                });

        //                TimeSpan duration = DateTime.UtcNow - start;
        //                Assert.GreaterOrEqual(duration, TimeSpan.FromSeconds(1));
        //                Assert.Less(duration, TimeSpan.FromSeconds(2));
        //                Assert.Greater(lastNo, 10);
        //            }
        //            finally
        //            {
        //                await server.ShutdownAsync();
        //            }
        //        }

        //        [Test]
        //        public async Task SequenceEnumerableWithCancellationTest()
        //        {
        //            var builder = new RpcServiceDefinitionsBuilder();
        //            builder.RegisterService<ISequenceService>();

        //            var (server, connection) = this.CreateServerAndConnection(builder);
        //            var sequenceServiceImpl = new SequenceServiceImpl();
        //            using (var publishedService = server.ServicePublisher.PublishSingleton<ISequenceService>(sequenceServiceImpl))
        //            {
        //                try
        //                {
        //                    server.Start();

        //                    var sequenceService = connection.GetServiceSingleton<ISequenceService>();

        //                    int lastNo = 0;
        //                    CancellationTokenSource cts = new CancellationTokenSource();

        //                    Assert.CatchAsync<OperationCanceledException>( async ()=>
        //                    {
        //                        await foreach (var sequenceData in sequenceService.GetSequenceAsCancellableEnumerable(1000,  TimeSpan.FromMilliseconds(20), 10, true, cts.Token))
        //                        {
        //                            Assert.AreEqual(lastNo + 1, sequenceData.SequenceNo);
        //                            lastNo = sequenceData.SequenceNo;
        //                            if (lastNo == 3)
        //                            {
        //                                cts.Cancel();
        //                            }
        //                        }
        //                    });

        //                    if (this.RoundTripCancellation)
        //                    {
        //                        Assert.Less(lastNo, 100);
        //                        Assert.GreaterOrEqual(lastNo, 3);
        //                    }
        //                    else
        //                    {
        //                        Assert.AreEqual(3, lastNo);
        //                    }

        //                    //
        //                    // Assert.IsTrue(await sequenceServiceImpl.GetIsCancelledAsync().DefaultTimeout());
        //                }
        //                finally
        //                {
        //                    await server.ShutdownAsync();
        //                }
        //            }
        //        }

        //        [TestCase(false)]
        //        [TestCase(true)]
        //        public async Task StreamingCall_UnpublishedInstance_ShouldThrow(bool cancellable)
        //        {
        //            var (server, connection) = this.CreateServerAndConnection();

        //            var serviceImpl = new SequenceServiceImpl();
        //            var isCancelledTask = serviceImpl.GetIsCancelledAsync();

        //            var publishedInstance = server.PublishInstance<ISequenceService>(serviceImpl, true);
        //            server.Start();
        //            try
        //            {
        //                var client = connection.GetServiceInstance(publishedInstance.Value);

        //                IAsyncEnumerable<SequenceData> sequence;
        //                if (cancellable)
        //                {
        //                    sequence = client.GetSequenceAsCancellableEnumerable(2, TimeSpan.FromSeconds(10), 1, false, CancellationToken.None);
        //                }
        //                else
        //                {
        //                    sequence = client.GetSequenceAsEnumerable(2, TimeSpan.FromSeconds(10), 1, false);
        //                }

        //                var sequenceEnum = sequence.GetAsyncEnumerator();
        //                await sequenceEnum.MoveNextAsync();
        //                var first = sequenceEnum.Current;

        //                // Unpublish the server side service after the first data has been received
        //                publishedInstance.Dispose();

        //                Assert.NotNull(first);
        //                Assert.ThrowsAsync<RpcServiceUnavailableException>(async () =>
        //                {
        //                    await sequenceEnum.MoveNextAsync().AsTask().TimeoutAfter(TimeSpan.FromSeconds(2));
        //                }
        //                );

        //                if (cancellable)
        //                {
        //                    Assert.IsTrue(await isCancelledTask.DefaultTimeout());
        //                }
        //            }
        //            finally
        //            {
        //                await server.ShutdownAsync();
        //            }
        //        }

        //        [TestCase(false)]
        //        [TestCase(true)]
        //        public async Task StreamingCall_UnpublishedAutoInstance_ShouldThrow(bool cancellable)
        //        {
        //            var definitionsBuilder = new RpcServiceDefinitionsBuilder();
        //            definitionsBuilder.RegisterService<ISequenceService>();
        //            var (server, connection) = this.CreateServerAndConnection(
        //                definitionsBuilder,
        //                configServerOptions: o=>
        //                {
        //                    o.AllowAutoPublish = true;
        //                });

        //            var providerImpl = new SequenceServiceProviderImpl();
        //            var isCancelledTask = ((SequenceServiceImpl)providerImpl.SequenceService).GetIsCancelledAsync();

        //            var publishedProvider = server.PublishInstance((ISequenceServiceProvider)providerImpl, true);
        //            server.Start();
        //            try
        //            {
        //                var providerClient = connection.GetServiceInstance(publishedProvider.Value);
        //                var sequenceClient = providerClient.SequenceService;

        //                IAsyncEnumerable<SequenceData> sequence;
        //                if (cancellable)
        //                {
        //                    sequence = sequenceClient.GetSequenceAsCancellableEnumerable(2, TimeSpan.FromSeconds(10), 1, false, CancellationToken.None);
        //                }
        //                else
        //                {
        //                    sequence = sequenceClient.GetSequenceAsEnumerable(2, TimeSpan.FromSeconds(10), 1, false);
        //                }

        //                var sequenceEnum = sequence.GetAsyncEnumerator();
        //                await sequenceEnum.MoveNextAsync();
        //                var first = sequenceEnum.Current;

        //                // Unpublish the server side service after the first data has been received
        //                providerImpl.ReleaseSequenceService();
        //                // Make sure that the auto-published instance is GCed (and thus unpublished).
        //                GC.Collect();
        //                GC.WaitForPendingFinalizers();
        //                GC.Collect();

        //                Assert.NotNull(first);

        //                Assert.ThrowsAsync<RpcServiceUnavailableException>(async () =>
        //                {
        //                    await sequenceEnum.MoveNextAsync().AsTask().DefaultTimeout();
        //                });

        //                if ( cancellable )
        //                {
        //                    Assert.IsTrue(await isCancelledTask.DefaultTimeout());
        //                }                
        //            }
        //            finally
        //            {
        //                await server.ShutdownAsync();
        //            }
        //        }
    }

    public class CallbackServiceImpl : ICallbackService
    {
        private TaskCompletionSource<bool> cancelledTcs = new TaskCompletionSource<bool>();

        internal Task<bool> GetIsCancelledAsync() => this.cancelledTcs.Task;


        public void SimpleCall() 
        {
        }

        public async Task PerformActionCallbacksAsync(int initialValue, int count, Action<CallbackData> callback)
        {
            for (int i = 0; i < count; i++)
            {
                callback(new CallbackData { Value = i + initialValue });
                await Task.Delay(10);
            }
        }

        public void PerformSyncActionCallbacks(int initialValue, int count, Action<CallbackData> callback)
        {
            for (int i = 0; i < count; i++)
            {
                callback(new CallbackData { Value = i + initialValue });
                Thread.Sleep(10);
            }
        }


        //public async Task PerformSimpleDelegateCallbacksAsync(int initialValue, int count, SimpleDelegate callback)
        //{
        //    for (int i = 0; i < count; i++)
        //    {
        //        callback(new CallbackData { Value = i + initialValue });
        //        await Task.Delay(10);
        //    }
        //}
    }

    // TODO: Handle non Action callbacks, and callbacks with multiple arguments with some kind of 
    // wrapper class (e.g. like below)
    //public class ActionWrapper
    //{
    //    private Action<CallbackData> callback;

    //    public ActionWrapper(Action<CallbackData> callback)
    //    {
    //        this.callback = callback;
    //    }

    //    internal void CallIt(CallbackData request)
    //    {
    //        callback(request);
    //    }
    //}
    //public class ActionWrapperIntInt
    //{
    //    private Action<RpcRequest<int,int>> callback;

    //    public ActionWrapperIntInt(Action<RpcRequest<int, int>> callback)
    //    {
    //        this.callback = callback;
    //    }

    //    internal void CallIt(int value1, int value2)
    //    {
    //        callback(new RpcRequest<int, int>(value1, value2));
    //    }
    //}
}
