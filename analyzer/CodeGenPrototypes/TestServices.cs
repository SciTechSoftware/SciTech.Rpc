using SciTech.Rpc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RpcTest
{

    [RpcService]
    public interface ISimpleService
    {
        int Add(int a, int b);

        Task<int> AddAsync(int a, int b, CancellationToken cancellationToken);

        IOtherService GetOtherService();

        string Name { get; set; }
    }


    [RpcService(ServerDefinitionType =typeof(ISimpleService))]
    public interface ISimpleServiceClient
    {
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



}

