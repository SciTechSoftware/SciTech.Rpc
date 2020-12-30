using Grpc.Core;
using Grpc.Core.Testing;
using Moq;
using NUnit.Framework;
using SciTech.Collections.Immutable;
using SciTech.Rpc.Grpc.Server.Internal;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.Tests.Grpc
{
    [TestFixture]
    public class GrpcStubTests
    {
        private static readonly IRpcSerializer DefaultSerializer = new ProtobufRpcSerializer();

        public GrpcStubTests()
        {
        }

        [Test]
        public async Task BlockingStubTest()
        {
            // TODO: Should use a mock instead.
            var serviceImpl = new TestBlockingSimpleServiceImpl();
            var binder = new TestMethodBinder();

            CreateSimpleServiceStub<IBlockingService>(serviceImpl, binder);
            var callContext = CreateServerCallContext(CancellationToken.None);

            var objectId = RpcObjectId.NewId();

            var setMethod = binder.methods.FirstOrDefault(p => p.Item1.Name == "SetValue");
            Assert.NotNull(setMethod);

            var setValueHandler = (UnaryServerMethod<RpcObjectRequest<double>, RpcResponse>)setMethod.Item2;
            await setValueHandler(new RpcObjectRequest<double>(objectId, 123.456), callContext);

            //await (Task<RpcResponse>)setMethod.Invoke(serviceStub, new object[] { new RpcObjectRequest<double>(objectId, 123.456), callContext });
            Assert.AreEqual(1, serviceImpl.nBlockingSetValue);

            var getMethod = binder.methods.FirstOrDefault(p => p.Item1.Name == "GetValue");
            Assert.NotNull(getMethod);

            var getValueHandler = (UnaryServerMethod<RpcObjectRequest, RpcResponse<double>>)getMethod.Item2;
            var getResponse = await getValueHandler(new RpcObjectRequest(objectId), callContext);
            Assert.AreEqual(1, serviceImpl.nBlockingGetValue);

            Assert.AreEqual(123.456, getResponse.Result);

            var addMethod = binder.methods.FirstOrDefault(p => p.Item1.Name == "Add");
            var addHandler = (UnaryServerMethod<RpcObjectRequest<int, int>, RpcResponse<int>>)addMethod.Item2;
            var addResponse = await addHandler(new RpcObjectRequest<int, int>(objectId, 8, 9), callContext);
            Assert.AreEqual(1, serviceImpl.nBlockingAdd);

            Assert.AreEqual(17, addResponse.Result);

        }

        [TearDown]
        public void Cleanup()
        {
            RpcStubOptions.TestDelayEventHandlers = false;

        }

        [Test]
        public async Task DeviceServiceTest()
        {
            var serviceImpl = new ThermostatServiceImpl();

            var implProviderMock = new Mock<IRpcServiceActivator>();
            implProviderMock.Setup(p => p.GetActivatedService<IDeviceService>(It.IsAny<IServiceProvider>(), It.IsAny<RpcObjectId>())).Returns(new ActivatedService<IDeviceService>( serviceImpl, false ));

            var callContext = CreateServerCallContext(CancellationToken.None);
            var binder = new TestMethodBinder();
            CreateSimpleServiceStub<IDeviceService>(serviceImpl, binder);

            var objectId = RpcObjectId.NewId();

            var getMethod = binder.methods.FirstOrDefault(p => p.Item1.Name == "GetDeviceAcoId");
            Assert.NotNull(getMethod);
            var getValueHandler = (UnaryServerMethod<RpcObjectRequest, RpcResponse<Guid>>)getMethod.Item2;
            var getResponse = await getValueHandler(new RpcObjectRequest(objectId), callContext);

            Assert.AreEqual(serviceImpl.DeviceAcoId, getResponse.Result);
        }

        [Test]
        public async Task EventHandlerTest()
        {
            var serviceImpl = new TestServiceWithEventsImpl();

            var binder = new TestMethodBinder();
            CreateSimpleServiceStub<ISimpleServiceWithEvents>(serviceImpl, binder);

            var beginHandler = binder.GetHandler<GrpcCore.ServerStreamingServerMethod<RpcObjectRequest, EventArgs>>("BeginValueChanged");

            var objectId = RpcObjectId.NewId();


            //var valueChangedMethod = stubType.GetMethod($"Begin{nameof(IServiceWithEvents.ValueChanged)}");
            var valueChangedStreamWriter = new ServerEventStreamWriter<EventArgs>(10);
            CancellationTokenSource eventListerCancellationSource = new CancellationTokenSource();
            var callContext = CreateServerCallContext(eventListerCancellationSource.Token);
            Task valueChangedTask = beginHandler(new RpcObjectRequest(objectId), valueChangedStreamWriter, callContext);


            var detailedValueChangedStreamWriter = new ServerEventStreamWriter<ValueChangedEventArgs>(10);

            var beginDetailedHandler = binder.GetHandler<GrpcCore.ServerStreamingServerMethod<RpcObjectRequest, ValueChangedEventArgs>>("BeginDetailedValueChanged");
            CancellationTokenSource detailedEventListerCancellationSource = new CancellationTokenSource();
            var detailedCallContext = CreateServerCallContext(detailedEventListerCancellationSource.Token);
            Task detailedValueChangedTask = beginDetailedHandler(new RpcObjectRequest(objectId), detailedValueChangedStreamWriter, detailedCallContext);

            await Task.WhenAny(Task.WhenAll(valueChangedStreamWriter.StartedTask, detailedValueChangedStreamWriter.StartedTask), Task.Delay(5000));
            Assert.IsTrue(valueChangedStreamWriter.StartedTask.IsCompletedSuccessfully());
            Assert.IsTrue(detailedValueChangedStreamWriter.StartedTask.IsCompletedSuccessfully());


            Task.Run(async () =>
            {
                List<Task> tasks = new List<Task>();
                for (int i = 0; i < 20; i++)
                {
                    tasks.Add(serviceImpl.SetValueAsync(18 + i));
                }
                await Task.WhenAll(tasks);
            }
            ).Forget();


            await Task.WhenAny(Task.WhenAll(valueChangedStreamWriter.CompletedTask, detailedValueChangedStreamWriter.CompletedTask), Task.Delay(5000));
            Assert.IsTrue(valueChangedStreamWriter.CompletedTask.IsCompletedSuccessfully());
            Assert.IsTrue(detailedValueChangedStreamWriter.CompletedTask.IsCompletedSuccessfully());

            eventListerCancellationSource.Cancel();
            detailedEventListerCancellationSource.Cancel();

            //var endProducerHandler = binder.GetHandler<GrpcCore.UnaryServerMethod<RpcObjectEventRequest, RpcResponse>>(nameof(RpcStub<object>.EndEventProducer));

            //var endTask = endProducerHandler(new RpcObjectEventRequest(objectId, eventProducerId), callContext);
            //var endDetailedTask = endProducerHandler(new RpcObjectEventRequest(objectId, detailedEventProducerId), callContext);
            //await Task.WhenAll(endTask, endDetailedTask);
            try
            {
                await Task.WhenAll(valueChangedTask, detailedValueChangedTask);
            }
            catch (OperationCanceledException) { }

            Assert.IsTrue(valueChangedTask.IsCompletedSuccessfully() || valueChangedTask.IsCanceled);
            Assert.IsTrue(detailedValueChangedTask.IsCompletedSuccessfully() || detailedValueChangedTask.IsCanceled);

            Assert.GreaterOrEqual(valueChangedStreamWriter.GetMessages().Length, 10);
            // Too timing dependent
            //Assert.Less(valueChangedStreamWriter.GetMessages().Length, 20);

            var detailedMessages = detailedValueChangedStreamWriter.GetMessages();
            Assert.GreaterOrEqual(detailedMessages.Length, 10);
            // Too timing dependent
            //Assert.Less(detailedValueChangedStreamWriter.GetMessages().Length, 20);

            int oldValue = 0;
            int newValue = 18;
            for (int i = 0; i < 10; i++)
            {
                var msg = detailedMessages[i];
                Assert.AreEqual(oldValue, msg.OldValue);
                Assert.AreEqual(newValue, msg.NewValue);
                oldValue = msg.NewValue;
                newValue = oldValue + 1;
            }
        }

        [Test]
        public async Task GenerateSimpleServiceStubTest()
        {
            var binder = new TestMethodBinder();
            CreateSimpleServiceStub<ISimpleService>(new TestSimpleServiceImpl(), binder);

            var callContext = CreateServerCallContext(CancellationToken.None);

            var addHandler = binder.GetHandler<GrpcCore.UnaryServerMethod<RpcObjectRequest<int, int>, RpcResponse<int>>>("Add");
            Assert.NotNull(addHandler);

            var objectId = RpcObjectId.NewId();
            var addResponseTask = addHandler.Invoke(new RpcObjectRequest<int, int>(objectId, 5, 6), callContext);

            var response = await addResponseTask.DefaultTimeout();
            Assert.AreEqual(11, response.Result);
        }

        [SetUp]
        public void Init()
        {
            RpcStubOptions.TestDelayEventHandlers = true;
        }

        /// <summary>
        /// Test that Get/Set works for a single service instance. Also 
        /// tests that method without parameter and method without return (only Task) works.
        /// </summary>
        /// <returns></returns>
        [Test(Description = "Test that Get/Set works for a single service instance")]
        public async Task SimpleServiceSetGetStubTest()
        {
            var binder = new TestMethodBinder();
            CreateSimpleServiceStub<ISimpleService>(new TestSimpleServiceImpl(), binder);
            var callContext = CreateServerCallContext(CancellationToken.None);

            var objectId = RpcObjectId.NewId();

            var setHandler = binder.GetHandler<UnaryServerMethod<RpcObjectRequest<double>, RpcResponse>>("SetValue");
            await setHandler(new RpcObjectRequest<double>(objectId, 123.456), callContext);

            var getHandler = binder.GetHandler<UnaryServerMethod<RpcObjectRequest, RpcResponse<double>>>("GetValue");
            var getResponse = await getHandler(new RpcObjectRequest(objectId), callContext);

            Assert.AreEqual(123.456, getResponse.Result);
        }

        protected static GrpcCore.ServerCallContext CreateServerCallContext(CancellationToken cancellationToken)
        {
            return TestServerCallContext.Create("TestMethod",
                       "host",
                       DateTime.MaxValue,
                       new Metadata(),
                       cancellationToken,
                       "peer",
                       null,
                       null,
                       null,
                       null,
                       null);

            //var constructors = typeof(GrpcCore.ServerCallContext).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance);

            //var ctor = constructors.FirstOrDefault(c => c.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
            //Assert.NotNull(ctor);

            //var ctorParams = ctor.GetParameters();

            //object[] ctorArgs = new object[ctorParams.Length];
            //int di = 0;
            //foreach (var param in ctorParams)
            //{
            //    if (param.ParameterType == typeof(CancellationToken))
            //    {
            //        ctorArgs[di] = cancellationToken;
            //    }
            //    else
            //    {
            //        ctorArgs[di] = null;
            //    }
            //    di++;
            //}

            //return (GrpcCore.ServerCallContext)ctor.Invoke(ctorArgs);
        }
        //private static void CreateSimpleServiceStub<TService>(TService serviceImpl, IGrpcMethodBinder methodBinder) where TService : class
        //{
        //    var builder = new GrpcServiceStubBuilder<TService>(new ProtobufSerializer());
        //    var hostMock = new Mock<IRpcServiceHostImpl>(MockBehavior.Strict);
        //    var serviceImplProviderMock = new Mock<IServiceImplProvider>();
        //    serviceImplProviderMock.Setup(p => p.GetServiceImpl<TService>(It.IsAny<RpcObjectId>())).Returns(serviceImpl);
        //    hostMock.Setup(p => p.ServiceImplProvider).Returns(serviceImplProviderMock.Object);
        //    hostMock.Setup(p => p.CallInterceptors).Returns(ImmutableArray<RpcHostCallInterceptor>.Empty);
        //    builder.GenerateOperationHandlers(hostMock.Object, methodBinder);
        //}

        private static void CreateSimpleServiceStub<TService>(TService serviceImpl, IGrpcMethodBinder methodBinder) where TService : class
        {
            var builder = new GrpcServiceStubBuilder<TService>(new RpcServiceOptions<TService> { Serializer = new ProtobufRpcSerializer() });

            var hostMock = new Mock<IRpcServerCore>(MockBehavior.Strict);

            var servicePublisherMock = new Mock<IRpcServicePublisher>(MockBehavior.Strict);
            var serviceDefinitionsProviderMock = new Mock<IRpcServiceDefinitionsProvider>(MockBehavior.Strict);
            serviceDefinitionsProviderMock.Setup(p => p.GetServiceOptions(It.IsAny<Type>())).Returns((RpcServerOptions)null);

            var serviceImplProviderMock = new Mock<IRpcServiceActivator>(MockBehavior.Strict);
            serviceImplProviderMock.Setup(p => p.GetActivatedService<TService>(It.IsAny<IServiceProvider>(), It.IsAny<RpcObjectId>())).Returns(new ActivatedService<TService>(serviceImpl, false));


            hostMock.Setup(h => h.ServicePublisher).Returns(servicePublisherMock.Object);
            hostMock.Setup(h => h.ServiceDefinitionsProvider).Returns(serviceDefinitionsProviderMock.Object);
            hostMock.Setup(h => h.ServiceActivator).Returns(serviceImplProviderMock.Object);
            hostMock.Setup(h => h.CallInterceptors).Returns(ImmutableArrayList<RpcServerCallInterceptor>.Empty);
            hostMock.Setup(h => h.ServiceProvider).Returns((IServiceProvider)null);
            hostMock.Setup(h => h.AllowAutoPublish).Returns(false);
            hostMock.Setup(h => h.Serializer).Returns(DefaultSerializer);
            hostMock.Setup(h => h.CustomFaultHandler).Returns((RpcServerFaultHandler)null);

            builder.GenerateOperationHandlers(hostMock.Object, methodBinder);
        }

        private class TestMethodBinder : IGrpcMethodBinder
        {
            internal List<ValueTuple<GrpcCore.IMethod, Delegate>> methods = new List<(IMethod, Delegate)>();

            public void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, UnaryServerMethod<TRequest, TResponse> handler)
                where TRequest : class
                where TResponse : class
            {
                this.methods.Add((method, handler));
            }

            public void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, ServerStreamingServerMethod<TRequest, TResponse> handler)
                where TRequest : class
                where TResponse : class
            {
                this.methods.Add((method, handler));
            }

            public THandler GetHandler<THandler>(string operationName) where THandler : Delegate
            {
                return this.methods.FirstOrDefault(p => p.Item1.Name == operationName).Item2 as THandler;
            }
        }
    }

    internal class ServerEventStreamWriter<T> : GrpcCore.IServerStreamWriter<T>
    {
        private readonly List<T> messageList = new List<T>();

        private readonly int nExpectedMessages;

        private TaskCompletionSource<bool> completedTcs = new TaskCompletionSource<bool>();

        private bool isFirst = true;

        private TaskCompletionSource<bool> startedTcs = new TaskCompletionSource<bool>();

        private object syncRoot = new object();

        internal ServerEventStreamWriter(int nExpectedMessages)
        {
            this.nExpectedMessages = nExpectedMessages;
        }

        public GrpcCore.WriteOptions WriteOptions { get; set; }

        internal Task CompletedTask => this.completedTcs.Task;

        internal Task StartedTask => this.startedTcs.Task;

        public async Task WriteAsync(T message)
        {
            bool done = false;

            if (this.isFirst)
            {
                this.isFirst = false;
                this.startedTcs.SetResult(true);
            }
            else
            {
                lock (this.syncRoot)
                {
                    this.messageList.Add(message);
                    done = this.messageList.Count == this.nExpectedMessages;
                }
            }

            await Task.Delay(1);

            if (done)
            {
                this.completedTcs.SetResult(true);
            }
        }

        internal T[] GetMessages()
        {
            lock (this.syncRoot)
            {
                return this.messageList.ToArray();
            }
        }
    }
}


