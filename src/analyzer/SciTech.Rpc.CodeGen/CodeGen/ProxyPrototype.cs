using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Lightweight.Client.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace SciTech.Rpc.Lightweight.Client
{
    public static class GeneratedRpcProxies
    {
        private static ImmutableArray<RpcProxyMethod> SciTech_Rpc_CodeGen_ISimpleService_Methods = ImmutableArray.Create<RpcProxyMethod>(
            new LightweightMethodDef<RpcObjectRequest<int, int>, RpcResponse<int>>(RpcMethodType.Unary, "SciTech.Rpc.CodeGen.SimpleService.Add", null, null),
            new LightweightMethodDef<RpcObjectRequest, RpcResponse<RpcObjectRef<SciTech.Rpc.CodeGen.IOtherService>>>(RpcMethodType.Unary, "SciTech.Rpc.CodeGen.SimpleService.GetOtherService", null, null)
            );


        public static readonly IReadOnlyDictionary<Type, Func<RpcProxyArgs, object>> ProxyFactories = new Dictionary<Type, Func<RpcProxyArgs, object>>()
        {
            { 
                typeof(SciTech.Rpc.CodeGen.ISimpleService), 
                new Func<RpcProxyArgs, object>(
                    args => new SciTech.Rpc.CodeGen.SimpleService(args, SciTech_Rpc_CodeGen_ISimpleService_Methods, new LightweightProxyCallDispatcher(args))) 
            }
        };
    }
}

namespace SciTech.Rpc.CodeGen
{

    [RpcService]
    public interface ISimpleService
    {
        int Add(int a, int b);

        Task<int> AddAsync(int a, int b, CancellationToken cancellationToken);

        IOtherService GetOtherService();

        string Name { get; set; }
    }

    [RpcService]
    public interface IOtherService
    {
        int SomethingElse();
    }

    //public interface IProxyCallDispatcher
    //{
    //    ValueTask<IAsyncStreamingServerCall<TResponse>> CallStreamingMethodAsync<TRequest, TResponse>(TRequest request, RpcProxyMethod method, CancellationToken cancellationToken)
    //        where TRequest : class
    //        where TResponse : class;

    //    TResponse CallUnaryMethodCore<TRequest, TResponse>(RpcProxyMethod methodDef, TRequest request, CancellationToken cancellationToken)
    //        where TRequest : class
    //        where TResponse : class;

    //    Task<TResponse> CallUnaryMethodCoreAsync<TRequest, TResponse>(RpcProxyMethod methodDef, TRequest request, CancellationToken cancellationToken)
    //        where TRequest : class
    //        where TResponse : class;

    //    void HandleCallException(RpcProxyMethod methodDef, Exception e);
    //}


    internal class SimpleService : RpcProxyBase, ISimpleService //where TMethodDef : RpcProxyMethod
    {
        private static readonly RpcClientFaultHandler ServiceFaultHandler =
            new RpcClientFaultHandler(
                null, new IRpcFaultExceptionFactory[]
                {
                    new RpcFaultExceptionFactory<MathFault>("MathFault")
                },
                new IRpcClientExceptionConverter[]
                {
                    new RpcFaultExceptionConverter<MathFault>()
                });


        public SimpleService(RpcProxyArgs proxyArgs, ImmutableArray<RpcProxyMethod> methods) : base(proxyArgs, methods)
        {
            
        }

        int ISimpleService.Add(int a, int b)
        {
            return this.CallUnaryMethod<RpcObjectRequest<int, int>, RpcResponse<int>, int>(
                this.proxyMethods[0],
                new RpcObjectRequest<int, int>(this.objectId, a, b),
                null,
                default);
        }

        Task<int> ISimpleService.AddAsync(int a, int b, CancellationToken cancellationToken)
        {
            return this.CallUnaryMethodAsync<RpcObjectRequest<int, int>, RpcResponse<int>, int>(
                this.proxyMethods[1],
                new RpcObjectRequest<int, int>(this.objectId, a, b),
                null,
                cancellationToken);
        }
        
        IOtherService ISimpleService.GetOtherService()
        {
            return this.CallUnaryMethod<RpcObjectRequest, RpcResponse<RpcObjectRef<IOtherService>>, IOtherService>(
                this.proxyMethods[2],
                new RpcObjectRequest(this.objectId),
                ServiceConverter<IOtherService>.Default,
                default);
        }

        string ISimpleService.Name
        {
            get
            {
                return this.CallUnaryMethod<RpcObjectRequest, RpcResponse<RpcObjectRef<IOtherService>>, IOtherService>(
                    this.proxyMethods[2],
                    new RpcObjectRequest(this.objectId),
                    ServiceConverter<IOtherService>.Default,
                    default);
            }
            set
            {
                this.CallUnaryMethod<RpcObjectRequest, RpcResponse<RpcObjectRef<IOtherService>>, IOtherService>(
                    this.proxyMethods[2],
                    new RpcObjectRequest(this.objectId),
                    ServiceConverter<IOtherService>.Default,
                    default);
            }
        }


    }

}
