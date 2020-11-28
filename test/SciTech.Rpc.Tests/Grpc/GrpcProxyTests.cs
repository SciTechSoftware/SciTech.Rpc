using Moq;
using NUnit.Framework;
using SciTech.Rpc.Client;
using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Grpc.Client.Internal;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Serialization;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.Tests.Grpc
{
    internal class AsyncStreamReader<T> : GrpcCore.IAsyncStreamReader<T>
    {
        private CancellationToken ct;

        private T current;

        private int intialDelayMs;

        private bool isFirst = true;

        private Func<ValueTuple<T, bool>> nextFunc;

        private int periodicDelayMs;

        internal AsyncStreamReader(Func<ValueTuple<T, bool>> nextFunc, int intialDelayMs, int periodicDelayMs, CancellationToken ct)
        {
            this.nextFunc = nextFunc;
            this.intialDelayMs = intialDelayMs;
            this.periodicDelayMs = periodicDelayMs;
            this.ct = ct;

        }

        public T Current => this.current;

        public void Dispose()
        {
        }

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            await Task.Delay(this.isFirst ? this.intialDelayMs : this.periodicDelayMs, this.ct).ConfigureAwait(false);
            this.isFirst = false;
            bool hasNext;
            (this.current, hasNext) = this.nextFunc();
            return hasNext;
        }
    }

    [TestFixture]
    public class GrpcProxyTests
    {

        [Test]
        public void BlockingProxyTest()
        {

            var (serviceInstance, callInvokerMock) = CreateServiceInstance<IBlockingService>();

            RpcObjectId objectId = ((IRpcService)serviceInstance).ObjectId;

            callInvokerMock.Setup(p => p.UnaryFunc<RpcObjectRequest<int, int>, RpcResponse<int>>("Add", It.IsAny<RpcObjectRequest<int, int>>()))
                .Returns((string op, RpcObjectRequest<int, int> r) =>
                {
                    Assert.AreEqual(objectId, r.Id);
                    return new RpcResponse<int> { Result = r.Value1 + r.Value2 };
                });

            var res = serviceInstance.Add(5, 6);
            Assert.AreEqual(11, res);

            callInvokerMock.Setup(p => p.UnaryFunc<RpcObjectRequest<double>, RpcResponse>("SetValue", It.IsAny<RpcObjectRequest<double>>()))
                .Returns((string op, RpcObjectRequest<double> r) =>
                {
                    Assert.AreEqual(objectId, r.Id);
                    Assert.AreEqual(123.45, r.Value1);
                    return new RpcResponse();
                });

            serviceInstance.Value = 123.45;

            callInvokerMock.Setup(p => p.UnaryFunc<RpcObjectRequest, RpcResponse<double>>("GetValue", It.IsAny<RpcObjectRequest>()))
                .Returns((string op, RpcObjectRequest r) =>
                {
                    Assert.AreEqual(objectId, r.Id);
                    return new RpcResponse<double> { Result = 543.21 };
                });

            var getRes = serviceInstance.Value;
            Assert.AreEqual(543.21, getRes);
        }

        [Test]
        public async Task EmptyDerivedProxyTest()
        {

            var (serviceInstance, callInvokerMock) = CreateServiceInstance<IEmptyDerivedService>();

            RpcObjectId objectId = ((IRpcService)serviceInstance).ObjectId;

            // Test Add and Sub, defined on different interfaces
            callInvokerMock.Setup(p => p.UnaryFunc<RpcObjectRequest<int, int>, RpcResponse<int>>("Add", It.IsAny<RpcObjectRequest<int, int>>()))
                .Returns((string op, RpcObjectRequest<int, int> r) =>
                {
                    Assert.AreEqual(objectId, r.Id);
                    return new RpcResponse<int> { Result = r.Value1 + r.Value2 };
                });

            var addRes = await serviceInstance.AddAsync(18, 19);
            Assert.AreEqual(18 + 19, addRes);

            callInvokerMock.Setup(p => p.UnaryFunc<RpcObjectRequest<int, int>, RpcResponse<int>>("Sub", It.IsAny<RpcObjectRequest<int, int>>()))
                .Returns((string op, RpcObjectRequest<int, int> r) =>
                {
                    Assert.AreEqual(objectId, r.Id);
                    return new RpcResponse<int> { Result = r.Value1 - r.Value2 };
                });

            var subRes = await serviceInstance.SubAsync(1218, 119);
            Assert.AreEqual(1218 - 119, subRes);
        }

        [Test]
        public async Task EventHandlersTest()
        {
            var (serviceInstance, callInvokerMock) = CreateServiceInstance<ISimpleServiceWithEvents>();

            RpcObjectId objectId = ((IRpcService)serviceInstance).ObjectId;

            callInvokerMock.Setup(p => p.ServerStreamingFunc<RpcObjectRequest, EventArgs>(It.IsAny<string>(), It.IsAny<RpcObjectRequest>(), It.IsAny<CancellationToken>()))
                .Returns((string op, RpcObjectRequest r, CancellationToken ct) =>
                {
                    Assert.AreEqual(objectId, r.Id);
                    return new AsyncStreamReader<EventArgs>(() => (EventArgs.Empty, true), 0, 20, ct);
                });

            int currValue = 4;
            callInvokerMock.Setup(p => p.ServerStreamingFunc<RpcObjectRequest, ValueChangedEventArgs>(It.IsAny<string>(), It.IsAny<RpcObjectRequest>(), It.IsAny<CancellationToken>()))
                .Returns((string op, RpcObjectRequest r, CancellationToken ct) =>
                {
                    Assert.AreEqual(objectId, r.Id);
                    return new AsyncStreamReader<ValueChangedEventArgs>(() =>
                    {
                        var oldValue = currValue++;
                        return (new ValueChangedEventArgs(currValue, oldValue), true);
                    }, 10, 10, ct); ;
                });

            //callInvokerMock.Setup(p => p.UnaryFunc<RpcObjectEventRequest, RpcResponse>("EndEventProducer", It.IsAny<RpcObjectEventRequest>()))
            //    .Returns(()=>new RpcResponse());

            int simpleChangeCount = 0;
            TaskCompletionSource<bool> simpleTcs = new TaskCompletionSource<bool>();
            EventHandler eventHandler = (s, e) => { simpleChangeCount++; if (simpleChangeCount == 2) { simpleTcs.SetResult(true); } };

            int? expectedOldValue = null;
            int detailedChangeCount = 0;
            TaskCompletionSource<bool> detailedTcs = new TaskCompletionSource<bool>();
            EventHandler<ValueChangedEventArgs> detailedEventHandler = (s, e) =>
            {
                if (expectedOldValue != null)
                {
                    Assert.AreEqual(expectedOldValue.Value, e.OldValue);
                }
                Assert.AreEqual(e.OldValue + 1, e.NewValue);
                expectedOldValue = e.NewValue;
                detailedChangeCount++;
                if (detailedChangeCount == 3)
                {
                    detailedTcs.SetResult(true);
                }
            };

            serviceInstance.ValueChanged += eventHandler;
            await ((IRpcService)serviceInstance).WaitForPendingEventHandlersAsync();

            serviceInstance.DetailedValueChanged += detailedEventHandler;

            await Task.WhenAny(Task.WhenAll(simpleTcs.Task, detailedTcs.Task), Task.Delay(1000));

            Assert.AreEqual(TaskStatus.RanToCompletion, simpleTcs.Task.Status);
            Assert.AreEqual(TaskStatus.RanToCompletion, detailedTcs.Task.Status);

            serviceInstance.ValueChanged -= eventHandler;
            serviceInstance.DetailedValueChanged -= detailedEventHandler;

            // TODO: Assert that Remove event method has been called for both handlers.
        }

        [Test]
        public async Task SimpleProxyTest()
        {

            var (serviceInstance, callInvokerMock) = CreateServiceInstance<ISimpleService>();

            RpcObjectId objectId = ((IRpcService)serviceInstance).ObjectId;

            callInvokerMock.Setup(p => p.UnaryFunc<RpcObjectRequest<int, int>, RpcResponse<int>>("Add", It.IsAny<RpcObjectRequest<int, int>>()))
                .Returns((string op, RpcObjectRequest<int, int> r) =>
                {
                    Assert.AreEqual(objectId, r.Id);
                    return new RpcResponse<int> { Result = r.Value1 + r.Value2 };
                });

            var m = serviceInstance.GetType().GetMethod("SimpleService.AddAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var mb = m.GetMethodBody();
            var il = mb.GetILAsByteArray();

            var res = await serviceInstance.AddAsync(5, 6);
            Assert.AreEqual(11, res);

            callInvokerMock.Setup(p => p.UnaryFunc<RpcObjectRequest<double>, RpcResponse>("SetValue", It.IsAny<RpcObjectRequest<double>>()))
                .Returns((string op, RpcObjectRequest<double> r) =>
                {
                    Assert.AreEqual(objectId, r.Id);
                    Assert.AreEqual(123.45, r.Value1);
                    return new RpcResponse();
                });

            await serviceInstance.SetValueAsync(123.45);

            callInvokerMock.Setup(p => p.UnaryFunc<RpcObjectRequest, RpcResponse<double>>("GetValue", It.IsAny<RpcObjectRequest>()))
                .Returns((string op, RpcObjectRequest r) =>
                {
                    Assert.AreEqual(objectId, r.Id);
                    return new RpcResponse<double> { Result = 543.21 };
                });

            var getRes = await serviceInstance.GetValueAsync();
            Assert.AreEqual(543.21, getRes);
        }

        //ModuleBuilder moduleBuilder;

        private (ModuleBuilder, Dictionary<string, int>) CreateModuleBuilder()
        {
            //if (this.moduleBuilder == null)
            //{
            var assemblyName = Guid.NewGuid().ToString();
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName), AssemblyBuilderAccess.RunAndCollect);
            return (assemblyBuilder.DefineDynamicModule(assemblyName), new Dictionary<string, int>());
            //}

            //return this.moduleBuilder;
        }

        private (TService, Mock<TestCallInvoker>) CreateServiceInstance<TService>() where TService : class
        {
            var proxyServiceDefinitionsProvider = new RpcProxyDefinitionsBuilder();
            var (moduleBuilder, definedTypes) = this.CreateModuleBuilder();
            var proxyBuilder = new RpcServiceProxyBuilder<GrpcProxyBase, GrpcProxyMethod>(RpcBuilderUtil.GetAllServices<TService>(true), moduleBuilder, definedTypes);

            var (proxyType, createMethodsFunc) = proxyBuilder.BuildObjectProxyType(new Type[] { typeof(GrpcProxyArgs), typeof(GrpcProxyMethod[]) });

            ValidateProxyType<TService>(proxyType);

            var factory = RpcServiceProxyBuilder<GrpcProxyBase, GrpcProxyMethod>.CreateObjectProxyFactory<GrpcProxyArgs>(proxyType);

            var callInvokerMock = new Mock<TestCallInvoker>(MockBehavior.Strict);
            var connectionMock = new Mock<IRpcServerConnection>(MockBehavior.Strict);
            connectionMock.Setup(m => m.Options).Returns(ImmutableRpcClientOptions.Empty);
            var serializer = new ProtobufRpcSerializer();

            var args = new GrpcProxyArgs
            (
                objectId: RpcObjectId.NewId(),
                connection: connectionMock.Object,
                callInvoker: callInvokerMock.Object,
                serializer: serializer,
                methodsCache: new GrpcMethodsCache(serializer),
                implementedServices: null,
                syncContext: null
            );

            //var proxyMethodsCache = new RpcProxyMethodsCache<GrpcProxyMethod>((Func<IRpcSerializer, GrpcProxyMethod[]>)proxyMethodsCreator.Compile());

            var proxyMethods = createMethodsFunc();
            var serviceInstance = factory(args, proxyMethods);

            return ((TService)(object)serviceInstance, callInvokerMock);
        }

        private void ValidateProxyType<TService>(Type proxyType)
        {
            // ValidateProxyType is pretty meaningless. 
            // The runtime will make sure that the proxyType is valid when an instance
            // of the type is created.
            Assert.IsTrue(typeof(IRpcService).IsAssignableFrom(proxyType));
            Assert.IsTrue(typeof(TService).IsAssignableFrom(proxyType));
        }
    }

    public abstract class TestCallInvoker : GrpcCore.CallInvoker
    {
        public override sealed GrpcCore.AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(GrpcCore.Method<TRequest, TResponse> method, string host, GrpcCore.CallOptions options)
        {
            throw new NotImplementedException();
        }

        public override sealed GrpcCore.AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(GrpcCore.Method<TRequest, TResponse> method, string host, GrpcCore.CallOptions options)
        {
            throw new NotImplementedException();
        }

        public override sealed GrpcCore.AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(GrpcCore.Method<TRequest, TResponse> method, string host, GrpcCore.CallOptions options, TRequest request)
        {
            return new GrpcCore.AsyncServerStreamingCall<TResponse>(ServerStreamingFunc<TRequest, TResponse>(method.Name, request, options.CancellationToken), Task.FromResult(new GrpcCore.Metadata()), null, null, () => { });
        }

        public override sealed GrpcCore.AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(GrpcCore.Method<TRequest, TResponse> method, string host, GrpcCore.CallOptions options, TRequest request)
        {
            Assert.NotNull(method);
            Assert.IsNotEmpty(method.Name);

            var response = UnaryFunc<TRequest, TResponse>(method.Name, request);
            return new GrpcCore.AsyncUnaryCall<TResponse>(Task.FromResult(response), null, null, null, () => { });

        }

        public override sealed TResponse BlockingUnaryCall<TRequest, TResponse>(GrpcCore.Method<TRequest, TResponse> method, string host, GrpcCore.CallOptions options, TRequest request)
        {
            Assert.NotNull(method);
            Assert.IsNotEmpty(method.Name);

            var response = UnaryFunc<TRequest, TResponse>(method.Name, request);
            return response;
        }

        public abstract GrpcCore.IAsyncStreamReader<TResponse> ServerStreamingFunc<TRequest, TResponse>(string operation, TRequest request, CancellationToken ct);

        public abstract TResponse UnaryFunc<TRequest, TResponse>(string operation, TRequest request);
    }
}
