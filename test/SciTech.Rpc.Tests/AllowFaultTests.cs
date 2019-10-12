using Moq;
using NUnit.Framework;
using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests
{


    [RpcService(AllowFault = true)]
    public interface IFaultService
    {
        int Add(int a, int b);

        void Append(string text);

        Task AppendRangeAsync(string[] texts);

        IAsyncEnumerable<string> GetStrings(int count);

        Task<int> SubAsync(int a, int b);
    }

    [RpcService(AllowFault = false)]
    public interface INoFaultService
    {
        int Add(int a, int b);

        void Append(string text);

        Task AppendRangeAsync(string[] texts);

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
        public void FaultServiceProxy_ShouldUseRpcResponseWithError()
        {
            var proxy = this.proxyAdapter.CreateProxy<IFaultService>();

            var addMethod = this.proxyAdapter.GetProxyMethod(proxy, "SciTech.Rpc.Tests.FaultService.Add");
            Assert.AreEqual(typeof(RpcResponseWithError<int>), addMethod.ResponseType);

            var subMethod = this.proxyAdapter.GetProxyMethod(proxy, "SciTech.Rpc.Tests.FaultService.Sub");
            Assert.AreEqual(typeof(RpcResponseWithError<int>), subMethod.ResponseType);

            var appendMethod = this.proxyAdapter.GetProxyMethod(proxy, "SciTech.Rpc.Tests.FaultService.Append");
            Assert.AreEqual(typeof(RpcResponseWithError), appendMethod.ResponseType);

            var appendRangeMethod = this.proxyAdapter.GetProxyMethod(proxy, "SciTech.Rpc.Tests.FaultService.AppendRange");
            Assert.AreEqual(typeof(RpcResponseWithError), appendRangeMethod.ResponseType);


            var getStringsMethod = this.proxyAdapter.GetProxyMethod(proxy, "SciTech.Rpc.Tests.FaultService.GetStrings");
            //Assert.AreEqual(typeof(RpcResponseWithError<string>), getStringsMethod.ResponseType);
        }

        [Test]
        public void FaultServiceStub_ShouldUseRpcResponseWithError()
        {
            var definitionsBuilder = new RpcServiceDefinitionsBuilder();
            var serializer = new ProtobufRpcSerializer();
            var serverMock = new Mock<IRpcServerImpl>();

            serverMock.SetupGet(m => m.ServiceDefinitionsProvider).Returns(definitionsBuilder);
            serverMock.SetupGet(m => m.Serializer).Returns(serializer);

            var methods = this.stubAdapter.GenerateMethodStubs<IFaultService>(serverMock.Object);

            var addMethod = this.stubAdapter.GetMethodStub(methods, "SciTech.Rpc.Tests.FaultService.Add");
            Assert.AreEqual(typeof(RpcResponseWithError<int>), this.stubAdapter.GetResponseType(addMethod));

            var subMethod = this.stubAdapter.GetMethodStub(methods, "SciTech.Rpc.Tests.FaultService.Sub");
            Assert.AreEqual(typeof(RpcResponseWithError<int>), this.stubAdapter.GetResponseType(subMethod));

            var appendMethod = this.stubAdapter.GetMethodStub(methods, "SciTech.Rpc.Tests.FaultService.Append");
            Assert.AreEqual(typeof(RpcResponseWithError), this.stubAdapter.GetResponseType(appendMethod));

            var appendRangeMethod = this.stubAdapter.GetMethodStub(methods, "SciTech.Rpc.Tests.FaultService.AppendRange");
            Assert.AreEqual(typeof(RpcResponseWithError), this.stubAdapter.GetResponseType(appendRangeMethod));


            var getStringsMethod = this.stubAdapter.GetMethodStub(methods, "SciTech.Rpc.Tests.FaultService.GetStrings");
            //Assert.AreEqual(typeof(RpcResponse<string>), getStringsMethod.ResponseType);
        }

        [Test]
        public void NoFaultServiceProxy_ShouldUseRpcResponse()
        {
            var proxy = this.proxyAdapter.CreateProxy<INoFaultService>();

            var addMethod = this.proxyAdapter.GetProxyMethod(proxy, "SciTech.Rpc.Tests.NoFaultService.Add");
            Assert.AreEqual(typeof(RpcResponse<int>), addMethod.ResponseType);

            var subMethod = this.proxyAdapter.GetProxyMethod(proxy, "SciTech.Rpc.Tests.NoFaultService.Sub");
            Assert.AreEqual(typeof(RpcResponse<int>), subMethod.ResponseType);

            var appendMethod = this.proxyAdapter.GetProxyMethod(proxy, "SciTech.Rpc.Tests.NoFaultService.Append");
            Assert.AreEqual(typeof(RpcResponse), appendMethod.ResponseType);

            var appendRangeMethod = this.proxyAdapter.GetProxyMethod(proxy, "SciTech.Rpc.Tests.NoFaultService.AppendRange");
            Assert.AreEqual(typeof(RpcResponse), appendRangeMethod.ResponseType);

            var getStringsMethod = this.proxyAdapter.GetProxyMethod(proxy, "SciTech.Rpc.Tests.NoFaultService.GetStrings");
            //Assert.AreEqual(typeof(RpcResponse<string>), getStringsMethod.ResponseType);
        }

        [Test]
        public void NoFaultServiceStub_ShouldUseRpcResponse()
        {
            var definitionsBuilder = new RpcServiceDefinitionsBuilder();
            var serializer = new ProtobufRpcSerializer();
            var serverMock = new Mock<IRpcServerImpl>();

            serverMock.SetupGet(m => m.ServiceDefinitionsProvider).Returns(definitionsBuilder);
            serverMock.SetupGet(m => m.Serializer).Returns(serializer);

            var methods = this.stubAdapter.GenerateMethodStubs<INoFaultService>(serverMock.Object);
            var addMethod = this.stubAdapter.GetMethodStub(methods, "SciTech.Rpc.Tests.NoFaultService.Add");
            Assert.AreEqual(typeof(RpcResponse<int>), this.stubAdapter.GetResponseType(addMethod));

            var subMethod = this.stubAdapter.GetMethodStub(methods, "SciTech.Rpc.Tests.NoFaultService.Sub");
            Assert.AreEqual(typeof(RpcResponse<int>), this.stubAdapter.GetResponseType(subMethod));

            var appendMethod = this.stubAdapter.GetMethodStub(methods, "SciTech.Rpc.Tests.NoFaultService.Append");
            Assert.AreEqual(typeof(RpcResponse), this.stubAdapter.GetResponseType(appendMethod));

            var appendRangeMethod = this.stubAdapter.GetMethodStub(methods, "SciTech.Rpc.Tests.NoFaultService.AppendRange");
            Assert.AreEqual(typeof(RpcResponse), this.stubAdapter.GetResponseType(appendRangeMethod));

            var getStringsMethod = this.stubAdapter.GetMethodStub(methods, "SciTech.Rpc.Tests.NoFaultService.GetStrings");
            //Assert.AreEqual(typeof(RpcResponse<string>), getStringsMethod.ResponseType);
        }
    }
}
