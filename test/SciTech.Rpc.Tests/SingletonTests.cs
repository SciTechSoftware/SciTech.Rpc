using Moq;
using NUnit.Framework;
using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Lightweight.Client;
using SciTech.Rpc.Lightweight.Client.Internal;
using SciTech.Rpc.Lightweight.Server.Internal;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using SciTech.Rpc.Tests.Lightweight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests
{
    /// <summary>
    /// Tests that singleton services does not include an object id, i.e. that RpcRequest<> is used instead of RpcObjectRequest<>.
    /// Currently only tests using the Lightweight proxy/stub generators, but since the code/type generation is 
    /// performed in the base classes, that shouldn't matter.
    /// </summary>
    public class SingletonTests
    {
        [Test]
        public void SingletonServiceProxy_ShouldUseRpcRequest()
        {
            var generator = new LightweightProxyGenerator();

            var factory = generator.GenerateObjectProxyFactory<ISingletonService>(null);
            var proxy = (LightweightProxyBase)factory(RpcObjectId.Empty, new TcpLightweightRpcConnection(new RpcServerConnectionInfo(RpcServerId.Empty)), null);

            var addMethod = proxy.proxyMethods.Single(m => m.OperationName == "SciTech.Rpc.Tests.SingletonService.Add");
            Assert.AreEqual(typeof(RpcRequest<int, int>), addMethod.RequestType);

            var subMethod = proxy.proxyMethods.Single(m => m.OperationName == "SciTech.Rpc.Tests.SingletonService.Sub");
            Assert.AreEqual(typeof(RpcRequest<int, int>), subMethod.RequestType);

            var getStringsMethod = proxy.proxyMethods.Single(m => m.OperationName == "SciTech.Rpc.Tests.SingletonService.GetStrings");
            Assert.AreEqual(typeof(RpcRequest<int>), getStringsMethod.RequestType);
        }

        [Test]
        public void SingletonServiceStub_ShouldUseRpcRequest()
        {
            var builder = new LightweightServiceStubBuilder<ISingletonService>(null); 

            //var binderMock = new Mock<ILightweightMethodBinder>();
            //binderMock.Setup(b => b.AddMethod(It.IsAny<LightweightMethodStub>())).Callback<LightweightMethodStub>(m => { 
            //    switch(m.OperationName)
            //    {
            //        case "SciTech.Rpc.Tests.SingletonService.Add":
            //            Assert.AreEqual(typeof(RpcRequest<int, int>), m.RequestType);
            //            break;
            //    }
            //});

            var binder = new TestLightweightMethodBinder();
            var serverMock = new Mock<IRpcServerImpl>();
            var definitionsBuilder = new RpcServiceDefinitionBuilder();
            var serializer = new ProtobufSerializer();
            serverMock.SetupGet(m => m.ServiceDefinitionsProvider).Returns(definitionsBuilder);
            serverMock.SetupGet(m => m.Serializer).Returns(serializer);

            var stub = builder.GenerateOperationHandlers(serverMock.Object, binder); ;

            var addMethod = binder.methods.Single(m => m.OperationName == "SciTech.Rpc.Tests.SingletonService.Add");
            Assert.AreEqual(typeof(RpcRequest<int, int>), addMethod.RequestType);

            var subMethod = binder.methods.Single(m => m.OperationName == "SciTech.Rpc.Tests.SingletonService.Sub");
            Assert.AreEqual(typeof(RpcRequest<int, int>), subMethod.RequestType);

            var getStringsMethod = binder.methods.Single(m => m.OperationName == "SciTech.Rpc.Tests.SingletonService.GetStrings");
            Assert.AreEqual(typeof(RpcRequest<int>), getStringsMethod.RequestType);

        }
    }

    [RpcService(IsSingleton =true)]
    public interface ISingletonService
    {
        int Add(int a, int b);

        Task<int> SubAsync(int a, int b);

        IAsyncEnumerable<string> GetStrings(int count);
    }
}
