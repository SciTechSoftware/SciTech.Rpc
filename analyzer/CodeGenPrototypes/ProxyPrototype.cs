public static class __SciTech_Rpc_Lightweight_GeneratedProxies
{
public static readonly System.Collections.Generic.IReadOnlyDictionary<System.Type, System.Func<SciTech.Rpc.Lightweight.Client.Internal.LightweightProxyArgs, object>> ProxyFactories =
    new System.Collections.Generic.Dictionary<System.Type, System.Func<SciTech.Rpc.Lightweight.Client.Internal.LightweightProxyArgs, object>>()
    {
            {
                typeof(RpcTest.ISimpleService),
                new System.Func<SciTech.Rpc.Lightweight.Client.Internal.LightweightProxyArgs, object>(
                    args => new __RpcTest_SimpleService(args))
            }
        };
}

internal static class __SciTech_Rpc_Lightweight_GeneratedMethodDefs
{
    internal static readonly SciTech.Rpc.Lightweight.Client.Internal.LightweightMethodDef RpcTest_SimpleService_Add =
        new SciTech.Rpc.Lightweight.Client.Internal.LightweightMethodDef<
            SciTech.Rpc.Internal.RpcObjectRequest<int, int>,
            SciTech.Rpc.Internal.RpcResponse<int>>(
                SciTech.Rpc.Internal.RpcMethodType.Unary, "RpcTest.SimpleService.Add", null, null);

    internal static readonly SciTech.Rpc.Lightweight.Client.Internal.LightweightMethodDef RpcTest_SimpleService_GetOtherService =
        new SciTech.Rpc.Lightweight.Client.Internal.LightweightMethodDef<
            SciTech.Rpc.Internal.RpcObjectRequest,
            SciTech.Rpc.Internal.RpcResponse<SciTech.Rpc.RpcObjectRef<RpcTest.IOtherService>>>(
                SciTech.Rpc.Internal.RpcMethodType.Unary, "RpcTest.SimpleService.GetOtherService", null, null);

    internal static readonly SciTech.Rpc.Lightweight.Client.Internal.LightweightMethodDef RpcTest_SimpleService_GetName =
        new SciTech.Rpc.Lightweight.Client.Internal.LightweightMethodDef<
            SciTech.Rpc.Internal.RpcObjectRequest,
            SciTech.Rpc.Internal.RpcResponse<string>>(
                SciTech.Rpc.Internal.RpcMethodType.Unary, "RpcTest.SimpleService.GetName", null, null);

    internal static readonly SciTech.Rpc.Lightweight.Client.Internal.LightweightMethodDef RpcTest_SimpleService_SetName =
        new SciTech.Rpc.Lightweight.Client.Internal.LightweightMethodDef<
            SciTech.Rpc.Internal.RpcObjectRequest<string>,
            SciTech.Rpc.Internal.RpcResponse>(
                SciTech.Rpc.Internal.RpcMethodType.Unary, "RpcTest.SimpleService.SetName", null, null);
}

internal class __RpcTest_SimpleService : SciTech.Rpc.Lightweight.Client.Internal.LightweightProxyBase, RpcTest.ISimpleService 
{
        //private static readonly RpcClientFaultHandler ServiceFaultHandler =
        //    new RpcClientFaultHandler(
        //        null, new IRpcFaultExceptionFactory[]
        //        {
        //            new RpcFaultExceptionFactory<MathFault>("MathFault")
        //        },
        //        new IRpcClientExceptionConverter[]
        //        {
        //            new RpcFaultExceptionConverter<MathFault>()
        //        });


    public __RpcTest_SimpleService(
        SciTech.Rpc.Lightweight.Client.Internal.LightweightProxyArgs proxyArgs) : base(proxyArgs)
    {

    }

    int RpcTest.ISimpleService.Add(int a, int b)
    {
        return this.CallUnaryMethod<SciTech.Rpc.Internal.RpcObjectRequest<int, int>, SciTech.Rpc.Internal.RpcResponse<int>, int>(
            __SciTech_Rpc_Lightweight_GeneratedMethodDefs.RpcTest_SimpleService_Add,
            new SciTech.Rpc.Internal.RpcObjectRequest<int, int>(this.objectId, a, b),
            null,
            default);
    }

    System.Threading.Tasks.Task<int> RpcTest.ISimpleService.AddAsync(int a, int b, System.Threading.CancellationToken cancellationToken)
    {       
        return this.CallUnaryMethodAsync<SciTech.Rpc.Internal.RpcObjectRequest<int, int>, SciTech.Rpc.Internal.RpcResponse<int>, int>(
            __SciTech_Rpc_Lightweight_GeneratedMethodDefs.RpcTest_SimpleService_Add,
            new SciTech.Rpc.Internal.RpcObjectRequest<int, int>(this.objectId, a, b),
            null,
            cancellationToken);
    }

    RpcTest.IOtherService RpcTest.ISimpleService.GetOtherService()
    {
        return this.CallUnaryMethod<SciTech.Rpc.Internal.RpcObjectRequest, SciTech.Rpc.Internal.RpcResponse<SciTech.Rpc.RpcObjectRef<RpcTest.IOtherService>>, RpcTest.IOtherService>(
            __SciTech_Rpc_Lightweight_GeneratedMethodDefs.RpcTest_SimpleService_GetOtherService,
            new SciTech.Rpc.Internal.RpcObjectRequest(this.objectId),
            SciTech.Rpc.Client.Internal.ServiceConverter<RpcTest.IOtherService>.Default,
            default);
    }

    string RpcTest.ISimpleService.Name
    {
        get
        {
            return this.CallUnaryMethod<SciTech.Rpc.Internal.RpcObjectRequest, SciTech.Rpc.Internal.RpcResponse<string>, string>(
                __SciTech_Rpc_Lightweight_GeneratedMethodDefs.RpcTest_SimpleService_GetName,
                new SciTech.Rpc.Internal.RpcObjectRequest(this.objectId),
                null,
                default);
        }
        set
        {
            this.CallUnaryVoidMethod(
                __SciTech_Rpc_Lightweight_GeneratedMethodDefs.RpcTest_SimpleService_SetName,
                new SciTech.Rpc.Internal.RpcObjectRequest<string>(this.objectId,value),
                default);
        }
    }
}


