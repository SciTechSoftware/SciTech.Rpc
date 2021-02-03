using NUnit.Framework;
using SciTech.Rpc.Lightweight.Server.Internal;
using SciTech.Rpc.Tests.Lightweight;
using SciTech.Rpc.Lightweight.Server;

namespace SciTech.Rpc.Tests
{
    public class CallbackStubTests
    {
        LightweightRpcServer server;
        TestLightweightMethodBinder binder;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            server = new LightweightRpcServer();
            binder = new TestLightweightMethodBinder();

        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            server.Dispose();
            server = null;
            binder = null;
        }

        [Test]
        public void CallbackWithReturn_Should_Throw()
        {
            var stubBuilder= new LightweightServiceStubBuilder<ICallbackWithReturnService>(null);
            
            Assert.Throws<RpcDefinitionException>(() => stubBuilder.GenerateOperationHandlers(server, binder));
        }

        [Test]
        public void UnsupportedCallback_Should_Throw()
        {
            var stubBuilder = new LightweightServiceStubBuilder<IUnsupportCallbackService1>(null);

            Assert.Throws<RpcDefinitionException>(() => stubBuilder.GenerateOperationHandlers(server, binder));
        }

        [Test]
        public void UnsupportedCallback2_Should_Throw()
        {
            var stubBuilder = new LightweightServiceStubBuilder<IUnsupportCallbackService2>(null);

            Assert.Throws<RpcDefinitionException>(() => stubBuilder.GenerateOperationHandlers(server, binder));
        }
        [Test]
        public void ValueTypeCallback_Should_Throw()
        {
            var stubBuilder = new LightweightServiceStubBuilder<IValueTypeCallbackService>(null);

            Assert.Throws<RpcDefinitionException>(() => stubBuilder.GenerateOperationHandlers(server, binder));
        }
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
