using Moq;
using NUnit.Framework;
using SciTech.IO;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Lightweight.Server;
using SciTech.Rpc.Lightweight.Server.Internal;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Serialization.Internal;
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

namespace SciTech.Rpc.Tests.Lightweight
{
    [TestFixture]
    public class LightweightStubTests
    {
        private static readonly IRpcSerializer DefaultSerializer = new ProtobufRpcSerializer();

        [Test]
        public void FailUnpublishedServiceProviderStubTest()
        {
            var binder = new TestLightweightMethodBinder();
            var definitionsProviderMock = new Mock<IRpcServiceDefinitionsProvider>(MockBehavior.Strict);
            definitionsProviderMock.Setup(p => p.IsServiceRegistered(It.IsAny<Type>())).Returns(true);
            definitionsProviderMock.Setup(p => p.GetServiceOptions(It.IsAny<Type>())).Returns((RpcServerOptions)null);

            RpcServicePublisher servicePublisher = new RpcServicePublisher(definitionsProviderMock.Object);
            var serviceImpl = new AutoPublishServiceProviderServiceImpl();

            var publishedServiceScope = servicePublisher.PublishInstance(serviceImpl);
            CreateSimpleServiceStub<IImplicitServiceProviderService>(servicePublisher, binder, false);

            LightweightMethodStub getServiceStub = binder.GetHandler<RpcObjectRequest<int>, RpcResponse<RpcObjectRef>>(
                "SciTech.Rpc.Tests.ImplicitServiceProviderService.GetSimpleService");

            Assert.NotNull(getServiceStub);

            var objectId = publishedServiceScope.Value.ObjectId;

            Assert.ThrowsAsync<RpcFailureException>( async ()=>
                await LightweightStubHelper.SendReceiveAsync<RpcObjectRequest<int>, RpcResponse<RpcObjectRef<ISimpleService>>>(
                   getServiceStub, new RpcObjectRequest<int>(objectId, 1), DefaultSerializer));
        }

        [Test]
        public async Task GenerateAutoPublishServiceProviderStubTest()
        {
            var binder = new TestLightweightMethodBinder();
            var definitionsProviderMock = new Mock<IRpcServiceDefinitionsProvider>(MockBehavior.Strict);
            definitionsProviderMock.Setup(p => p.IsServiceRegistered(It.IsAny<Type>())).Returns(true);
            definitionsProviderMock.Setup(p => p.GetServiceOptions(It.IsAny<Type>())).Returns((RpcServerOptions)null);

            RpcServicePublisher servicePublisher = new RpcServicePublisher(definitionsProviderMock.Object);
            var serviceImpl = new AutoPublishServiceProviderServiceImpl();

            var publishedServiceScope = servicePublisher.PublishInstance(serviceImpl);
            CreateSimpleServiceStub<IImplicitServiceProviderService>(servicePublisher, binder, true);

            LightweightMethodStub getServiceStub = binder.GetHandler<RpcObjectRequest<int>, RpcResponse<RpcObjectRef>>(
                "SciTech.Rpc.Tests.ImplicitServiceProviderService.GetSimpleService");
            Assert.NotNull(getServiceStub);

            var objectId = publishedServiceScope.Value.ObjectId;

            var getServiceResponse = await LightweightStubHelper.SendReceiveAsync<RpcObjectRequest<int>, RpcResponse<RpcObjectRef>>(
                getServiceStub, new RpcObjectRequest<int>(objectId, 0), DefaultSerializer);
            Assert.NotNull(getServiceResponse.Result);

            var actualServiceRef = servicePublisher.GetPublishedInstance(serviceImpl.GetSimpleService(0));

            Assert.AreEqual(actualServiceRef, getServiceResponse.Result);

            LightweightMethodStub getServicesStub = binder.GetHandler<RpcObjectRequest, RpcResponse<RpcObjectRef[]>>(
                "SciTech.Rpc.Tests.ImplicitServiceProviderService.GetSimpleServices");
            Assert.NotNull(getServicesStub);

            var getServicesResponse = await LightweightStubHelper.SendReceiveAsync<RpcObjectRequest, RpcResponse<RpcObjectRef[]>>(
                getServicesStub, new RpcObjectRequest(objectId), DefaultSerializer);
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
            var binder = new TestLightweightMethodBinder();
            var definitionsProviderMock = new Mock<IRpcServiceDefinitionsProvider>(MockBehavior.Strict);
            definitionsProviderMock.Setup(p => p.IsServiceRegistered(It.IsAny<Type>())).Returns(true);
            definitionsProviderMock.Setup(p => p.GetServiceOptions(It.IsAny<Type>())).Returns((RpcServerOptions)null);

            _ = definitionsProviderMock.Object.GetServiceOptions(typeof(IImplicitServiceProviderService));

            RpcServicePublisher servicePublisher = new RpcServicePublisher(definitionsProviderMock.Object);
            var serviceImpl = new ImplicitServiceProviderServiceImpl(servicePublisher);

            var publishedServiceScope = servicePublisher.PublishInstance(serviceImpl);
            CreateSimpleServiceStub<IImplicitServiceProviderService>(servicePublisher, binder, false);

            var objectId = publishedServiceScope.Value.ObjectId;

            LightweightMethodStub getServiceStub = binder.GetHandler<RpcObjectRequest, RpcResponse<RpcObjectRef>>(
                "SciTech.Rpc.Tests.ImplicitServiceProviderService.GetFirstSimpleService");

            Assert.NotNull(getServiceStub);

            var getServiceResponse = await LightweightStubHelper.SendReceiveAsync<RpcObjectRequest, RpcResponse<RpcObjectRef>>(
                getServiceStub, new RpcObjectRequest(objectId), DefaultSerializer);
            Assert.NotNull(getServiceResponse.Result);

            var actualServiceRef = servicePublisher.GetPublishedInstance(serviceImpl.FirstSimpleService);

            Assert.AreEqual(actualServiceRef, getServiceResponse.Result);


        }

        [Test]
        public async Task GenerateImplicitServiceProviderStubTest()
        {
            var binder = new TestLightweightMethodBinder();
            var definitionsProviderMock = new Mock<IRpcServiceDefinitionsProvider>(MockBehavior.Strict);
            definitionsProviderMock.Setup(p => p.IsServiceRegistered(It.IsAny<Type>())).Returns(true);
            definitionsProviderMock.Setup(p => p.GetServiceOptions(It.IsAny<Type>())).Returns((RpcServerOptions)null);

            RpcServicePublisher servicePublisher = new RpcServicePublisher(definitionsProviderMock.Object);
            var serviceImpl = new ImplicitServiceProviderServiceImpl(servicePublisher);

            var publishedServiceScope = servicePublisher.PublishInstance(serviceImpl);
            CreateSimpleServiceStub<IImplicitServiceProviderService>(servicePublisher, binder, false);

            var objectId = publishedServiceScope.Value.ObjectId;

            LightweightMethodStub getServiceStub = binder.GetHandler<RpcObjectRequest<int>, RpcResponse<RpcObjectRef>>(
                "SciTech.Rpc.Tests.ImplicitServiceProviderService.GetSimpleService");

            Assert.NotNull(getServiceStub);

            var getServiceResponse = await LightweightStubHelper.SendReceiveAsync<RpcObjectRequest<int>, RpcResponse<RpcObjectRef>>(
                getServiceStub, new RpcObjectRequest<int>(objectId, 1), DefaultSerializer);
            Assert.NotNull(getServiceResponse.Result);

            var actualServiceRef = servicePublisher.GetPublishedInstance(serviceImpl.GetSimpleService(1));

            Assert.AreEqual(actualServiceRef, getServiceResponse.Result);


        }

        [Test]
        public async Task GenerateSimpleBlockingServiceStubTest()
        {
            var binder = new TestLightweightMethodBinder();
            CreateSimpleServiceStub<IBlockingService>(new TestBlockingSimpleServiceImpl(), binder);

            LightweightMethodStub addStub = binder.GetHandler<RpcObjectRequest<int, int>, RpcResponse<int>>("SciTech.Rpc.Tests.BlockingService.Add");
            Assert.NotNull(addStub);

            var objectId = RpcObjectId.NewId();

            var request = new RpcObjectRequest<int, int>(objectId, 5, 6);
            RpcResponse<int> addResponse = await LightweightStubHelper.SendReceiveAsync<RpcObjectRequest<int, int>, RpcResponse<int>>(addStub, request, DefaultSerializer);
            Assert.AreEqual(11, addResponse.Result);

            LightweightMethodStub setStub = binder.GetHandler<RpcObjectRequest<double>, RpcResponse>("SciTech.Rpc.Tests.BlockingService.SetValue");
            Assert.NotNull(setStub);
            var setResponse = await LightweightStubHelper.SendReceiveAsync<RpcObjectRequest<double>, RpcResponse>(setStub, new RpcObjectRequest<double>(objectId, 20), DefaultSerializer);
            Assert.NotNull(setResponse);

            LightweightMethodStub getStub = binder.GetHandler<RpcObjectRequest, RpcResponse<double>>("SciTech.Rpc.Tests.BlockingService.GetValue");
            Assert.NotNull(getStub);
            var getResponse = await LightweightStubHelper.SendReceiveAsync<RpcObjectRequest, RpcResponse<double>>(getStub, new RpcObjectRequest(objectId), DefaultSerializer);
            Assert.AreEqual(20, getResponse.Result);

        }

        [Test]
        public async Task GenerateSimpleServiceStubTest()
        {
            var binder = new TestLightweightMethodBinder();
            CreateSimpleServiceStub<ISimpleService>(new TestSimpleServiceImpl(), binder);

            LightweightMethodStub addStub = binder.GetHandler<RpcObjectRequest<int, int>, RpcResponse<int>>("SciTech.Rpc.Tests.SimpleService.Add");
            Assert.NotNull(addStub);

            var objectId = RpcObjectId.NewId();
            var request = new RpcObjectRequest<int, int>(objectId, 5, 6);
            RpcResponse<int> addResponse = await LightweightStubHelper.SendReceiveAsync<RpcObjectRequest<int, int>, RpcResponse<int>>(addStub, request, DefaultSerializer);

            Assert.AreEqual(11, addResponse.Result);

            LightweightMethodStub setStub = binder.GetHandler<RpcObjectRequest<double>, RpcResponse>("SciTech.Rpc.Tests.SimpleService.SetValue");
            Assert.NotNull(setStub);
            var setResponse = await LightweightStubHelper.SendReceiveAsync<RpcObjectRequest<double>, RpcResponse>(setStub, new RpcObjectRequest<double>(objectId, 20), DefaultSerializer);
            Assert.NotNull(setResponse);

            LightweightMethodStub getStub = binder.GetHandler<RpcObjectRequest, RpcResponse<double>>("SciTech.Rpc.Tests.SimpleService.GetValue");
            Assert.NotNull(getStub);
            var getResponse = await LightweightStubHelper.SendReceiveAsync<RpcObjectRequest, RpcResponse<double>>(getStub, new RpcObjectRequest(objectId), DefaultSerializer);
            Assert.AreEqual(20, getResponse.Result);

        }

        private static IRpcServiceDefinitionsProvider CreateDefinitionsProviderMock()
        {
            var serviceDefinitionsProviderMock = new Mock<IRpcServiceDefinitionsProvider>(MockBehavior.Strict);
            serviceDefinitionsProviderMock.Setup(p => p.CustomFaultHandler).Returns((RpcServerFaultHandler)null);
            serviceDefinitionsProviderMock.Setup(p => p.GetServiceOptions(It.IsAny<Type>())).Returns((RpcServerOptions)null);
            return serviceDefinitionsProviderMock.Object;
        }

        private void CreateSimpleServiceStub<TService>(TService serviceImpl, ILightweightMethodBinder methodBinder) where TService : class
        {
            var builder = new LightweightServiceStubBuilder<TService>(new RpcServiceOptions<TService> { Serializer = DefaultSerializer });

            IRpcServiceDefinitionsProvider serviceDefinitionsProvider = CreateDefinitionsProviderMock();

            var hostMock = new Mock<IRpcServerImpl>(MockBehavior.Strict);

            var servicePublisherMock = new Mock<IRpcServicePublisher>(MockBehavior.Strict);
            var serviceImplProviderMock = new Mock<IRpcServiceActivator>(MockBehavior.Strict);
            serviceImplProviderMock.Setup(p => p.GetActivatedService<TService>(It.IsAny<IServiceProvider>(), It.IsAny<RpcObjectId>())).Returns(new ActivatedService<TService>( serviceImpl,false));

            hostMock.Setup(h => h.ServicePublisher).Returns(servicePublisherMock.Object);
            hostMock.Setup(h => h.ServiceActivator).Returns(serviceImplProviderMock.Object);
            hostMock.Setup(h => h.ServiceDefinitionsProvider).Returns(serviceDefinitionsProvider);
            hostMock.Setup(h => h.CallInterceptors).Returns(ImmutableArray<RpcServerCallInterceptor>.Empty);
            hostMock.Setup(h => h.AllowAutoPublish).Returns(false);
            hostMock.Setup(h => h.Serializer).Returns(DefaultSerializer);
            hostMock.Setup(h => h.CustomFaultHandler).Returns((RpcServerFaultHandler)null);

            builder.GenerateOperationHandlers(hostMock.Object, methodBinder);
        }

        private void CreateSimpleServiceStub<TService>(RpcServicePublisher servicePublisher, ILightweightMethodBinder methodBinder, bool allowAutoPublish) where TService : class
        {
            var builder = new LightweightServiceStubBuilder<TService>(new RpcServiceOptions<TService> { Serializer = DefaultSerializer });

            var hostMock = new Mock<IRpcServerImpl>(MockBehavior.Strict);
            hostMock.Setup(h => h.ServicePublisher).Returns(servicePublisher);
            hostMock.Setup(h => h.ServiceActivator).Returns(servicePublisher);
            hostMock.Setup(h => h.ServiceDefinitionsProvider).Returns(servicePublisher.DefinitionsProvider);
            hostMock.Setup(h => h.AllowAutoPublish).Returns(allowAutoPublish);
            hostMock.Setup(h => h.Serializer).Returns(DefaultSerializer);
            hostMock.Setup(h => h.CustomFaultHandler).Returns((RpcServerFaultHandler)null);
            hostMock.Setup(h => h.HandleCallException(It.IsAny<Exception>(), It.IsAny<IRpcSerializer>()));
            hostMock.Setup(p => p.CallInterceptors).Returns(ImmutableArray<RpcServerCallInterceptor>.Empty);


            builder.GenerateOperationHandlers(hostMock.Object, methodBinder);
        }
    }

    internal static class LightweightStubHelper
    {
        internal static async Task<TResponse> SendReceiveDatagramAsync<TRequest, TResponse>(
            LightweightRpcEndPoint endPoint,
            IRpcConnectionHandler connectionHandler,
            string operationName,
            TRequest request,
            IRpcSerializer serializer,
            CancellationToken cancellationToken = default)
            where TRequest : class
            where TResponse : class
        {
            byte[] requestData = GetRequestData(operationName, 1, request, serializer);

            var responseData = await connectionHandler.HandleDatagramAsync(endPoint, requestData, cancellationToken);
            Assert.NotNull(responseData);

            TResponse response = GetResponseFromData<TResponse>(serializer, responseData);

            return response;
        }

        internal static TResponse GetResponseFromData<TResponse>(IRpcSerializer serializer, byte[] responseData) where TResponse : class
        {
            TResponse response = null;
            if (LightweightRpcFrame.TryRead(responseData, 65536, out var responseFrame) == RpcFrameState.Full)
            {
                response = (TResponse)serializer.Deserialize(responseFrame.Payload, typeof(TResponse));
            }

            return response;
        }

        internal static byte[] GetRequestData<TRequest>(string operationName, int messageNumber, TRequest request, IRpcSerializer serializer) 
            where TRequest : class
        {
            byte[] requestData;
            using (var writer = new LightweightRpcFrameWriter(65536))
            {
                var frame = new LightweightRpcFrame(RpcFrameType.UnaryRequest, messageNumber, operationName, RpcOperationFlags.None, 0, null);
                requestData = writer.WriteFrame(frame, request, serializer);
            }

            return requestData;
        }

        internal static async Task<TResponse> SendReceiveAsync<TRequest, TResponse>(LightweightMethodStub methodStub, TRequest request, IRpcSerializer serializer)
            where TRequest : class
            where TResponse : class
        {
            TResponse response;

            var context = new LightweightCallContext(new TestRpcEndPoint(), ImmutableArray<KeyValuePair<string, ImmutableArray<byte>>>.Empty, CancellationToken.None);
            var requestPipe = new Pipe();
            var responsePipe = new Pipe();
            var duplexPipe = new DirectDuplexPipe(requestPipe.Reader, responsePipe.Writer);
            using (var pipeline = new TestPipeline(duplexPipe))
            {
                var payload = new ReadOnlySequence<byte>(serializer.Serialize(request));

                var frame = new LightweightRpcFrame(RpcFrameType.UnaryRequest, null, 1, methodStub.OperationName, RpcOperationFlags.None, 0, payload, null);

                await methodStub.HandleMessage(pipeline, frame, null, context);

                var readResult = await responsePipe.Reader.ReadAsync();
                var buffer = readResult.Buffer;
                bool hasResponseFrame = LightweightRpcFrame.TryRead(ref buffer, 65536, out var responseFrame) == RpcFrameState.Full;
                Assert.IsTrue(hasResponseFrame);

                response = (TResponse)serializer.Deserialize(responseFrame.Payload, typeof(TResponse));

                return response;
            }
        }

        private class TestPipeline : RpcPipeline
        {
            public TestPipeline(IDuplexPipe pipe) : base(pipe, 65536, 65536, true)
            {

            }

            protected override ValueTask OnReceiveAsync(in LightweightRpcFrame frame)
            {
                throw new NotImplementedException();
            }

            protected override Task OnReceiveLargeFrameAsync(LightweightRpcFrame frame)
            {
                throw new NotImplementedException();
            }
        }
    }

    internal class TestLightweightMethodBinder : ILightweightMethodBinder
    {
        internal List<LightweightMethodStub> methods = new List<LightweightMethodStub>();

        public void AddMethod(LightweightMethodStub methodStub)
        {
            this.methods.Add(methodStub);
        }

        public LightweightMethodStub GetHandler<TRequest, TResponse>(string operationName) where TRequest : IObjectRequest
        {
            return this.methods.SingleOrDefault(p => p.OperationName == operationName
                && p.RequestType.Equals(typeof(TRequest))
                && p.ResponseType.Equals(typeof(TResponse)));
        }
    }

    internal class TestRpcEndPoint : LightweightRpcEndPoint
    {
        public override string DisplayName => "Test";

        public override string HostName => "test";

        public override RpcServerConnectionInfo GetConnectionInfo(RpcServerId serverId)
        {
            return new RpcServerConnectionInfo(serverId);
        }

        protected internal override ILightweightRpcListener CreateListener(IRpcConnectionHandler discoveryHandler, int maxRequestSize, int maxResponseSize)
        {
            throw new NotImplementedException();
        }
    }
}
