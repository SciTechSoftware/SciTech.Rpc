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
using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Lightweight.Client.Internal;
using SciTech.Rpc.Lightweight.Server.Internal;
using SciTech.Rpc.Tests.Lightweight;
using SciTech.Rpc.Lightweight.Server;

namespace SciTech.Rpc.Tests
{

    [RpcService]
    public interface ICallbackService
    {
        Task PerformActionCallbacksAsync(int value, int count, Action<CallbackData> callback);
        
        void PerformSyncActionCallbacks(int value, int count, Action<CallbackData> callback);

        Task PerformDelayedCallbacksAsync(int count, TimeSpan delay, int delayFrequency, bool initialDelay, Action<CallbackData> callback);

        Task PerformCancellableCallbacksAsync(int count, TimeSpan delay, int delayFrequency, bool initialDelay, Action<CallbackData> callback, CancellationToken cancellationToken );


        // Task PerformSimpleDelegateCallbacksAsync(int value, int count, SimpleDelegate callback);
    }

    [RpcService]
    public interface ICallbackWithReturnService
    {
        Task<int> PerformActionCallbacksAsync(int value, int count, Action<CallbackData> callback);
    }

    [RpcService]
    public interface IUnsupportCallbackService1
    {
        Task PerformActionCallbacksAsync(int value, int count, SimpleDelegate callback);
    }

    [RpcService]
    public interface IUnsupportCallbackService2
    {
        Task PerformActionCallbacksAsync(int value, int count, Action<int,int> callback);
    }

    [RpcService]
    public interface IValueTypeCallbackService
    {
        Task PerformActionCallbacksAsync(int value, int count, Action<int> callback);
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
        public async Task CallbackMethod_Should_InvokeAction()
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
        public async Task SyncCallbackMethod_Should_InvokeAction()
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


        [Test]
        public async Task CallbackTimeoutTest()
        {
            var builder = new RpcServiceDefinitionsBuilder();
            builder.RegisterService<ICallbackService>();

            var (server, connection) = this.CreateServerAndConnection(builder,
                null,
                o =>
                {
                    o.StreamingCallTimeout = TimeSpan.FromSeconds(1);
                });

            var callbackServiceImpl = new CallbackServiceImpl();
            using var publishedService = server.ServicePublisher.PublishSingleton<ICallbackService>(callbackServiceImpl);
            try
            {
                server.Start();

                var callbackService = connection.GetServiceSingleton<ICallbackService>();


                var exceptionExpression = Is.InstanceOf<TimeoutException>();
                if (this.ConnectionType == RpcConnectionType.NetGrpc)
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
                    await callbackService.PerformDelayedCallbacksAsync(2000, TimeSpan.FromMilliseconds(1), 1, true,
                        callbackData =>
                        {
                            Assert.AreEqual(lastNo + 1, callbackData.Value);
                            lastNo = callbackData.Value;
                        }).DefaultTimeout();
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
        public async Task CallbackWithCancellationTest()
        {
            var builder = new RpcServiceDefinitionsBuilder();
            builder.RegisterService<ICallbackService>();

            var (server, connection) = this.CreateServerAndConnection(builder);
            var callbackServiceImpl = new CallbackServiceImpl();
            using (var publishedService = server.ServicePublisher.PublishSingleton<ICallbackService>(callbackServiceImpl))
            {
                try
                {
                    server.Start();

                    var callbackService = connection.GetServiceSingleton<ICallbackService>();

                    int lastNo = 0;
                    CancellationTokenSource cts = new CancellationTokenSource();

                    Assert.CatchAsync<OperationCanceledException>(async () =>
                   {
                       await callbackService.PerformCancellableCallbacksAsync(1000, TimeSpan.FromMilliseconds(20), 10, true,
                           callbackData =>
                           {
                               Assert.AreEqual(lastNo + 1, callbackData.Value);
                               lastNo = callbackData.Value;
                               if (lastNo == 3)
                               {
                                   cts.Cancel();
                               }
                           }, cts.Token);
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
        public async Task CallbackCall_UnpublishedInstance_ShouldThrow(bool cancellable)
        {
            var (server, connection) = this.CreateServerAndConnection();

            var serviceImpl = new CallbackServiceImpl();
            var isCancelledTask = serviceImpl.GetIsCancelledAsync();

            var publishedInstance = server.PublishInstance<ICallbackService>(serviceImpl, true);
            server.Start();
            try
            {
                var client = connection.GetServiceInstance(publishedInstance.Value);

                TaskCompletionSource<int> firstTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task callbackTask;
                if (cancellable)
                {
                    callbackTask = client.PerformCancellableCallbacksAsync(2, TimeSpan.FromSeconds(10), 1, false, Callback, CancellationToken.None);
                }
                else
                {
                    callbackTask = client.PerformDelayedCallbacksAsync(2, TimeSpan.FromSeconds(10), 1, false, Callback);
                }

                await firstTcs.Task.DefaultTimeout();

                // Unpublish the server side service after the first data has been received
                publishedInstance.Dispose();

                Assert.ThrowsAsync<RpcServiceUnavailableException>(async () =>
                {
                    await callbackTask.TimeoutAfter(TimeSpan.FromSeconds(2));
                }
                );

                if (cancellable)
                {
                    Assert.IsTrue(await isCancelledTask.DefaultTimeout());
                }

                void Callback(CallbackData callbackData)
                {
                    firstTcs.TrySetResult(callbackData.Value);
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
            definitionsBuilder.RegisterService<ICallbackService>();
            var (server, connection) = this.CreateServerAndConnection(
                definitionsBuilder,
                configServerOptions: o =>
                {
                    o.AllowAutoPublish = true;
                });

            var providerImpl = new SequenceServiceProviderImpl();
            var isCancelledTask = ((CallbackServiceImpl)providerImpl.CallbackService).GetIsCancelledAsync();

            var publishedProvider = server.PublishInstance((ISequenceServiceProvider)providerImpl, true);
            server.Start();
            try
            {
                var providerClient = connection.GetServiceInstance(publishedProvider.Value);
                var callbackClient = providerClient.CallbackService;

                TaskCompletionSource<int> firstTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task callbackTask;
                if (cancellable)
                {
                    callbackTask = callbackClient.PerformCancellableCallbacksAsync(2, TimeSpan.FromSeconds(10), 1, false, Callback, CancellationToken.None);
                }
                else
                {
                    callbackTask = callbackClient.PerformDelayedCallbacksAsync(2, TimeSpan.FromSeconds(10), 1, false, Callback);
                }

                await firstTcs.Task.DefaultTimeout();

                // Release server side references to callback service implementation.
                providerImpl.ReleaseServices();

                // Make sure that the auto-published instance is GCed (and thus unpublished).
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                Assert.ThrowsAsync<RpcServiceUnavailableException>(async () =>
                {
                    await callbackTask.TimeoutAfter(TimeSpan.FromSeconds(2));
                });

                if (cancellable)
                {
                    Assert.IsTrue(await isCancelledTask.DefaultTimeout());
                }

                void Callback(CallbackData callbackData)
                {
                    firstTcs.TrySetResult(callbackData.Value);
                }
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }


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

        public Task PerformDelayedCallbacksAsync(int count, TimeSpan delay, int delayFrequency, bool initialDelay, Action<CallbackData> callback)
        {
            static async Task PerformDelayedCallbacksAsync(int count, TimeSpan delay, int delayFrequency, bool initialDelay, Action<CallbackData> callback)
            {
                for (int i = 0; i < count; i++)
                {
                    if ((i > 0 || initialDelay) && delayFrequency != 0 && i % delayFrequency == 0)
                    {
                        await Task.Delay(delay);
                    }
                    else
                    {
                        await Task.Yield();
                    }

                    callback(new CallbackData { Value = i + 1 });
                }
            }

            return PerformDelayedCallbacksAsync(count, delay, delayFrequency, initialDelay, callback);
        }


        public Task PerformCancellableCallbacksAsync(int count, TimeSpan delay, int delayFrequency, bool initialDelay, Action<CallbackData> callback, CancellationToken cancellationToken)
        {
            return StaticPerformCancellableCallbacksAsync(count, delay, delayFrequency, initialDelay, this.cancelledTcs, callback, cancellationToken);
            
            static async Task StaticPerformCancellableCallbacksAsync(
                int count, TimeSpan delay, int delayFrequency, bool initialDelay, TaskCompletionSource<bool> cancelledTcs,
                Action<CallbackData> callback, CancellationToken cancellationToken)
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

                    callback(new CallbackData { Value = i + 1 });
                }

                cancelledTcs.TrySetResult(cancellationToken.IsCancellationRequested);
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
    //public class ActionWrapperCallbackData
    //{
    //    private Action<CallbackData> callback;

    //    public ActionWrapperCallbackData(Action<CallbackData> callback)
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
