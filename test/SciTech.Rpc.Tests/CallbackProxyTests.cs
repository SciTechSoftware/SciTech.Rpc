using NUnit.Framework;
using SciTech.Rpc.Lightweight.Client.Internal;

namespace SciTech.Rpc.Tests
{
    public class CallbackProxyTests
    {
        [Test]
        public void CallbackWithReturn_Should_Throw()
        {
            var proxyGenerator = new LightweightProxyGenerator();

            Assert.Throws<RpcDefinitionException>(() => proxyGenerator.GenerateObjectProxyFactory<ICallbackWithReturnService>(null, null));
        }

        [Test]
        public void UnsupportedCallback_Should_Throw()
        {
            var proxyGenerator = new LightweightProxyGenerator();

            Assert.Throws<RpcDefinitionException>(() => proxyGenerator.GenerateObjectProxyFactory<IUnsupportCallbackService1>(null, null));
        }

        [Test]
        public void UnsupportedCallback2_Should_Throw()
        {
            var proxyGenerator = new LightweightProxyGenerator();

            Assert.Throws<RpcDefinitionException>(() => proxyGenerator.GenerateObjectProxyFactory<IUnsupportCallbackService2>(null, null));
        }
        [Test]
        public void ValueTypeCallback_Should_Throw()
        {
            var proxyGenerator = new LightweightProxyGenerator();

            Assert.Throws<RpcDefinitionException>(() => proxyGenerator.GenerateObjectProxyFactory<IValueTypeCallbackService>(null, null));
        }
    }
}
