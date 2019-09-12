using SciTech.Rpc.Client.Internal;
using System;
using System.Linq;

namespace SciTech.Rpc.Tests
{
    public interface IProxyTestAdapter<TMethodDef> where TMethodDef : RpcProxyMethod
    {
        RpcProxyBase<TMethodDef> CreateProxy<TService>() where TService : class;

        TMethodDef GetProxyMethod(RpcProxyBase<TMethodDef> proxy, string methodName);
    }
}
