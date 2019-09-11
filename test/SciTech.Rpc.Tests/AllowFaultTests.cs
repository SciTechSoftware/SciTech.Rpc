using Moq;
using NUnit.Framework;
using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests
{

    [RpcService(AllowFault = false)]
    public interface INoFaultService
    {
        int Add(int a, int b);

        IAsyncEnumerable<string> GetStrings(int count);

        Task<int> SubAsync(int a, int b);
    }

    /// <summary>
    /// </summary>
    public abstract class AllowFaultTests<TMethodDef> where TMethodDef : RpcProxyMethod
    {
        private IProxyTestAdapter<TMethodDef> proxyAdapter;

        private IStubTestAdapter stubAdapter;

        protected AllowFaultTests(IProxyTestAdapter<TMethodDef> proxyAdapter, IStubTestAdapter stubAdapter)
        {
            this.proxyAdapter = proxyAdapter;
            this.stubAdapter = stubAdapter;
        }

        [Test]
        public void NoFaultServiceProxy_ShouldUseRpcResponse()
        {
            var proxy = this.proxyAdapter.CreateProxy<INoFaultService>();

            var addMethod = this.proxyAdapter.GetProxyMethod(proxy, "SciTech.Rpc.Tests.NoFaultService.Add");
            Assert.AreEqual(typeof(RpcResponse<int>), this.proxyAdapter.GetResponseType(addMethod));

            var subMethod = this.proxyAdapter.GetProxyMethod(proxy, "SciTech.Rpc.Tests.NoFaultService.Sub");
            Assert.AreEqual(typeof(RpcResponse<int>), this.proxyAdapter.GetResponseType(subMethod));

            var getStringsMethod = this.proxyAdapter.GetProxyMethod(proxy, "SciTech.Rpc.Tests.NoFaultService.GetStrings");
            //Assert.AreEqual(typeof(RpcResponse<string>), getStringsMethod.ResponseType);
        }

        [Test]
        public void NoFaultServiceStub_ShouldUseRpcResponse()
        {
            var definitionsBuilder = new RpcServiceDefinitionBuilder();
            var serializer = new ProtobufSerializer();
            var serverMock = new Mock<IRpcServerImpl>();

            serverMock.SetupGet(m => m.ServiceDefinitionsProvider).Returns(definitionsBuilder);
            serverMock.SetupGet(m => m.Serializer).Returns(serializer);

            var methods = this.stubAdapter.GenerateMethodStubs<INoFaultService>(serverMock.Object);
            var addMethod = this.stubAdapter.GetMethodStub(methods, "SciTech.Rpc.Tests.NoFaultService.Add");
            Assert.AreEqual(typeof(RpcResponse<int>), this.stubAdapter.GetResponseType(addMethod));

            var subMethod = this.stubAdapter.GetMethodStub(methods, "SciTech.Rpc.Tests.NoFaultService.Sub");
            Assert.AreEqual(typeof(RpcResponse<int>), this.stubAdapter.GetResponseType(subMethod));

            var getStringsMethod = this.stubAdapter.GetMethodStub(methods, "SciTech.Rpc.Tests.NoFaultService.GetStrings");
            //Assert.AreEqual(typeof(RpcResponse<string>), getStringsMethod.ResponseType);
        }
    }
}
