using Moq;
using NUnit.Framework;
using SciTech.IO;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Pipelines.Internal;
using SciTech.Rpc.Pipelines.Server.Internal;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Pipelines
{
    [TestFixture]
    public class PipelinesStubTests
    {
        private static readonly IRpcSerializer DefaultSerializer = new ProtobufSerializer();

        [Test]
        public async Task FailUnpublishedServiceProviderStubTest()
        {
            var binder = new TestMethodBinder();
            var definitionsProviderMock = new Mock<IRpcServiceDefinitionsProvider>(MockBehavior.Strict);
            definitionsProviderMock.Setup(p => p.IsServiceRegistered(It.IsAny<Type>())).Returns(true);
            RpcServicePublisher servicePublisher = new RpcServicePublisher(definitionsProviderMock.Object);
            var serviceImpl = new AutoPublishServiceProviderServiceImpl();

            var publishedServiceScope = servicePublisher.PublishInstance(serviceImpl);
            CreateSimpleServiceStub<IImplicitServiceProviderService>(servicePublisher, binder, false);

            PipelinesMethodStub getServiceStub = binder.GetHandler<RpcObjectRequest<int>, RpcResponse<RpcObjectRef>>(
                "SciTech.Rpc.Tests.ImplicitServiceProviderService.GetSimpleService");

            Assert.NotNull(getServiceStub);

            var objectId = publishedServiceScope.Value.ObjectId;

            var response = await SendReceiveAsync<RpcObjectRequest<int>, RpcResponse<RpcObjectRef<ISimpleService>>>(
                   getServiceStub, new RpcObjectRequest<int>(objectId, 1));

            Assert.AreEqual(WellKnownRpcErrors.Failure, response.Error?.ErrorType);
        }

        [Test]
        public async Task GenerateAutoPublishServiceProviderStubTest()
        {
            var binder = new TestMethodBinder();
            var definitionsProviderMock = new Mock<IRpcServiceDefinitionsProvider>(MockBehavior.Strict);
            definitionsProviderMock.Setup(p => p.IsServiceRegistered(It.IsAny<Type>())).Returns(true);
            RpcServicePublisher servicePublisher = new RpcServicePublisher(definitionsProviderMock.Object);
            var serviceImpl = new AutoPublishServiceProviderServiceImpl();

            var publishedServiceScope = servicePublisher.PublishInstance(serviceImpl);
            CreateSimpleServiceStub<IImplicitServiceProviderService>(servicePublisher, binder, true);

            PipelinesMethodStub getServiceStub = binder.GetHandler<RpcObjectRequest<int>, RpcResponse<RpcObjectRef>>(
                "SciTech.Rpc.Tests.ImplicitServiceProviderService.GetSimpleService");
            Assert.NotNull(getServiceStub);

            var objectId = publishedServiceScope.Value.ObjectId;

            var getServiceResponse = await SendReceiveAsync<RpcObjectRequest<int>, RpcResponse<RpcObjectRef>>(
                getServiceStub, new RpcObjectRequest<int>(objectId, 0));
            Assert.NotNull(getServiceResponse.Result);

            var actualServiceRef = servicePublisher.GetPublishedInstance(serviceImpl.GetSimpleService(0));

            Assert.AreEqual(actualServiceRef, getServiceResponse.Result);

            PipelinesMethodStub getServicesStub = binder.GetHandler<RpcObjectRequest, RpcResponse<RpcObjectRef[]>>(
                "SciTech.Rpc.Tests.ImplicitServiceProviderService.GetSimpleServices");
            Assert.NotNull(getServicesStub);

            var getServicesResponse = await SendReceiveAsync<RpcObjectRequest, RpcResponse<RpcObjectRef[]>>(
                getServicesStub, new RpcObjectRequest(objectId));
            Assert.NotNull(getServiceResponse.Result);

            var actualServiceRefs = servicePublisher.GetPublishedServiceInstances(serviceImpl.GetSimpleServices(), false);

            Assert.AreEqual(actualServiceRefs.Count, getServicesResponse.Result.Length);
            for (int i = 0; i < actualServiceRefs.Count; i++)
            {
                Assert.AreEqual(actualServiceRefs[i], getServicesResponse.Result[i]);
            }
        }

        [Test]
        public async Task GenerateImplicitServiceProviderPropertyStubTest()
        {
            var binder = new TestMethodBinder();
            var definitionsProviderMock = new Mock<IRpcServiceDefinitionsProvider>(MockBehavior.Strict);
            definitionsProviderMock.Setup(p => p.IsServiceRegistered(It.IsAny<Type>())).Returns(true);
            RpcServicePublisher servicePublisher = new RpcServicePublisher(definitionsProviderMock.Object);
            var serviceImpl = new ImplicitServiceProviderServiceImpl(servicePublisher);

            var publishedServiceScope = servicePublisher.PublishInstance(serviceImpl);
            CreateSimpleServiceStub<IImplicitServiceProviderService>(servicePublisher, binder, false);

            var objectId = publishedServiceScope.Value.ObjectId;

            PipelinesMethodStub getServiceStub = binder.GetHandler<RpcObjectRequest, RpcResponse<RpcObjectRef>>(
                "SciTech.Rpc.Tests.ImplicitServiceProviderService.GetFirstSimpleService");

            Assert.NotNull(getServiceStub);

            var getServiceResponse = await SendReceiveAsync<RpcObjectRequest, RpcResponse<RpcObjectRef>>(
                getServiceStub, new RpcObjectRequest(objectId));
            Assert.NotNull(getServiceResponse.Result);

            var actualServiceRef = servicePublisher.GetPublishedInstance(serviceImpl.FirstSimpleService);

            Assert.AreEqual(actualServiceRef, getServiceResponse.Result);


        }

        [Test]
        public async Task GenerateImplicitServiceProviderStubTest()
        {
            var binder = new TestMethodBinder();
            var definitionsProviderMock = new Mock<IRpcServiceDefinitionsProvider>(MockBehavior.Strict);
            definitionsProviderMock.Setup(p => p.IsServiceRegistered(It.IsAny<Type>())).Returns(true);
            RpcServicePublisher servicePublisher = new RpcServicePublisher(definitionsProviderMock.Object);
            var serviceImpl = new ImplicitServiceProviderServiceImpl(servicePublisher);

            var publishedServiceScope = servicePublisher.PublishInstance(serviceImpl);
            CreateSimpleServiceStub<IImplicitServiceProviderService>(servicePublisher, binder, false);

            var objectId = publishedServiceScope.Value.ObjectId;

            PipelinesMethodStub getServiceStub = binder.GetHandler<RpcObjectRequest<int>, RpcResponse<RpcObjectRef>>(
                "SciTech.Rpc.Tests.ImplicitServiceProviderService.GetSimpleService");

            Assert.NotNull(getServiceStub);

            var getServiceResponse = await SendReceiveAsync<RpcObjectRequest<int>, RpcResponse<RpcObjectRef>>(
                getServiceStub, new RpcObjectRequest<int>(objectId, 1));
            Assert.NotNull(getServiceResponse.Result);

            var actualServiceRef = servicePublisher.GetPublishedInstance(serviceImpl.GetSimpleService(1));

            Assert.AreEqual(actualServiceRef, getServiceResponse.Result);


        }

        [Test]
        public async Task GenerateSimpleBlockingServiceStubTest()
        {
            var binder = new TestMethodBinder();
            CreateSimpleServiceStub<IBlockingService>(new TestBlockingSimpleServiceImpl(), binder);

            PipelinesMethodStub addStub = binder.GetHandler<RpcObjectRequest<int, int>, RpcResponse<int>>("SciTech.Rpc.Tests.BlockingService.Add");
            Assert.NotNull(addStub);

            var objectId = RpcObjectId.NewId();

            var request = new RpcObjectRequest<int, int>(objectId, 5, 6);
            RpcResponse<int> addResponse = await SendReceiveAsync<RpcObjectRequest<int, int>, RpcResponse<int>>(addStub, request);
            Assert.AreEqual(11, addResponse.Result);

            PipelinesMethodStub setStub = binder.GetHandler<RpcObjectRequest<double>, RpcResponse>("SciTech.Rpc.Tests.BlockingService.SetValue");
            Assert.NotNull(setStub);
            var setResponse = await SendReceiveAsync<RpcObjectRequest<double>, RpcResponse>(setStub, new RpcObjectRequest<double>(objectId, 20));
            Assert.NotNull(setResponse);

            PipelinesMethodStub getStub = binder.GetHandler<RpcObjectRequest, RpcResponse<double>>("SciTech.Rpc.Tests.BlockingService.GetValue");
            Assert.NotNull(getStub);
            var getResponse = await SendReceiveAsync<RpcObjectRequest, RpcResponse<double>>(getStub, new RpcObjectRequest(objectId));
            Assert.AreEqual(20, getResponse.Result);

        }

        [Test]
        public async Task GenerateSimpleServiceStubTest()
        {
            var binder = new TestMethodBinder();
            CreateSimpleServiceStub<ISimpleService>(new TestSimpleServiceImpl(), binder);

            PipelinesMethodStub addStub = binder.GetHandler<RpcObjectRequest<int, int>, RpcResponse<int>>("SciTech.Rpc.Tests.SimpleService.Add");
            Assert.NotNull(addStub);

            var objectId = RpcObjectId.NewId();
            var request = new RpcObjectRequest<int, int>(objectId, 5, 6);
            RpcResponse<int> addResponse = await SendReceiveAsync<RpcObjectRequest<int, int>, RpcResponse<int>>(addStub, request);

            Assert.AreEqual(11, addResponse.Result);

            PipelinesMethodStub setStub = binder.GetHandler<RpcObjectRequest<double>, RpcResponse>("SciTech.Rpc.Tests.SimpleService.SetValue");
            Assert.NotNull(setStub);
            var setResponse = await SendReceiveAsync<RpcObjectRequest<double>, RpcResponse>(setStub, new RpcObjectRequest<double>(objectId, 20));
            Assert.NotNull(setResponse);

            PipelinesMethodStub getStub = binder.GetHandler<RpcObjectRequest, RpcResponse<double>>("SciTech.Rpc.Tests.SimpleService.GetValue");
            Assert.NotNull(getStub);
            var getResponse = await SendReceiveAsync<RpcObjectRequest, RpcResponse<double>>(getStub, new RpcObjectRequest(objectId));
            Assert.AreEqual(20, getResponse.Result);

        }

        private void CreateSimpleServiceStub<TService>(TService serviceImpl, IPipelinesMethodBinder methodBinder) where TService : class
        {
            var builder = new PipelinesServiceStubBuilder<TService>(new RpcServiceOptions<TService> { Serializer = DefaultSerializer });

            var serviceDefinitionsProviderMock = new Mock<IRpcServiceDefinitionsProvider>(MockBehavior.Strict);
            serviceDefinitionsProviderMock.Setup(p => p.CustomFaultHandler).Returns((RpcServerFaultHandler)null);

            var hostMock = new Mock<IRpcServerImpl>(MockBehavior.Strict);

            var servicePublisherMock = new Mock<IRpcServicePublisher>(MockBehavior.Strict);
            var serviceImplProviderMock = new Mock<IRpcServiceActivator>(MockBehavior.Strict);
            serviceImplProviderMock.Setup(p => p.GetServiceImpl<TService>(It.IsAny<IServiceProvider>(), It.IsAny<RpcObjectId>())).Returns(serviceImpl);

            hostMock.Setup(h => h.ServicePublisher).Returns(servicePublisherMock.Object);
            hostMock.Setup(h => h.ServiceImplProvider).Returns(serviceImplProviderMock.Object);
            hostMock.Setup(h => h.ServiceDefinitionsProvider).Returns(serviceDefinitionsProviderMock.Object);
            hostMock.Setup(h => h.CallInterceptors).Returns(ImmutableArray<RpcServerCallInterceptor>.Empty);
            hostMock.Setup(h => h.AllowAutoPublish).Returns(false);
            hostMock.Setup(h => h.Serializer).Returns(DefaultSerializer);
            hostMock.Setup(h => h.CustomFaultHandler).Returns((RpcServerFaultHandler)null);

            builder.GenerateOperationHandlers(hostMock.Object, methodBinder);
        }

        private void CreateSimpleServiceStub<TService>(RpcServicePublisher servicePublisher, IPipelinesMethodBinder methodBinder, bool allowAutoPublish) where TService : class
        {
            var builder = new PipelinesServiceStubBuilder<TService>(new RpcServiceOptions<TService> { Serializer = DefaultSerializer });

            var serviceDefinitionsProviderMock = new Mock<IRpcServiceDefinitionsProvider>(MockBehavior.Strict);
            serviceDefinitionsProviderMock.Setup(p => p.CustomFaultHandler).Returns((RpcServerFaultHandler)null);

            var hostMock = new Mock<IRpcServerImpl>(MockBehavior.Strict);
            hostMock.Setup(h => h.ServicePublisher).Returns(servicePublisher);
            hostMock.Setup(h => h.ServiceImplProvider).Returns(servicePublisher);
            hostMock.Setup(h => h.ServiceDefinitionsProvider).Returns(serviceDefinitionsProviderMock.Object);
            hostMock.Setup(h => h.AllowAutoPublish).Returns(allowAutoPublish);
            hostMock.Setup(h => h.Serializer).Returns(DefaultSerializer);
            hostMock.Setup(h => h.CustomFaultHandler).Returns((RpcServerFaultHandler)null);

            hostMock.Setup(p => p.CallInterceptors).Returns(ImmutableArray<RpcServerCallInterceptor>.Empty);


            builder.GenerateOperationHandlers(hostMock.Object, methodBinder);
        }

        private async Task<TResponse> SendReceiveAsync<TRequest, TResponse>(PipelinesMethodStub methodStub, TRequest request)
            where TRequest : class
            where TResponse : class
        {
            TResponse response;

            var context = new PipelinesCallContext(ImmutableArray<KeyValuePair<string, string>>.Empty, CancellationToken.None);
            var requestPipe = new Pipe();
            var responsePipe = new Pipe();
            var duplexPipe = new DirectDuplexPipe(requestPipe.Reader, responsePipe.Writer);
            using (var pipeline = new TestPipeline(duplexPipe))
            {

                var payload = new ReadOnlySequence<byte>(DefaultSerializer.ToBytes(request));

                var frame = new RpcPipelinesFrame(RpcFrameType.UnaryRequest, 1, methodStub.OperationName, RpcOperationFlags.None, 0, payload, null);

                await methodStub.HandleMessage(pipeline, frame, null, context);

                var readResult = await responsePipe.Reader.ReadAsync();
                var buffer = readResult.Buffer;
                bool hasResponseFrame = RpcPipelinesFrame.TryRead(ref buffer, 65536, out var responseFrame);
                Assert.IsTrue(hasResponseFrame);

                using (var responsePayloadStream = responseFrame.Payload.AsStream())
                {
                    response = (TResponse)DefaultSerializer.FromStream(typeof(TResponse), responsePayloadStream);
                }

                return response;
            }
        }

        private class TestMethodBinder : IPipelinesMethodBinder
        {
            internal List<PipelinesMethodStub> methods = new List<PipelinesMethodStub>();

            public void AddMethod(PipelinesMethodStub methodStub)
            {
                this.methods.Add(methodStub);
            }

            public PipelinesMethodStub GetHandler<TRequest, TResponse>(string operationName) where TRequest : IObjectRequest
            {
                return this.methods.SingleOrDefault(p => p.OperationName == operationName
                    && p.RequestType.Equals(typeof(TRequest))
                    && p.ResponseType.Equals(typeof(TResponse)));
            }
        }

        private class TestPipeline : RpcPipeline
        {
            public TestPipeline(IDuplexPipe pipe) : base(pipe)
            {

            }

            protected override ValueTask OnReceiveAsync(in RpcPipelinesFrame frame)
            {
                throw new NotImplementedException();
            }
        }
    }
}
