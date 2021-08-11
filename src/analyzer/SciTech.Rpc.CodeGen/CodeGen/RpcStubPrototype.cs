using SciTech.Rpc.Internal;
using SciTech.Rpc.Lightweight.Server.Internal;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.CodeGen
{
    //class SimpleServiceStub 
    //{
    //    private ISimpleService service;

    //    public static void Create()
    //    {
    //        new LightweightMethodStub<RpcRequest<int,int>,RpcRequest<int>>("SciTech.Rpc.CodeGen.SimpleService.Add", )
    //    }
    //    int CallAdd(ISimpleService service, RpcObjectRequest<int,int> request, CancellationToken cancellationToken)
    //    {
    //        return service.Add(request.Value1, request.Value2);

    //    }

        
    //    int ISimpleService.Add(int a, int b)
    //    {

    //        throw new NotImplementedException();
    //    }

    //    Task<int> ISimpleService.AddAsync(int a, int b, CancellationToken cancellationToken)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    IOtherService ISimpleService.GetOtherService()
    //    {
    //        throw new NotImplementedException();
    //    }
    //}
}
