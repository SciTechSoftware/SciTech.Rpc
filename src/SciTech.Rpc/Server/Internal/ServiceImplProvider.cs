using System;
using System.Collections.Generic;
using System.Linq;

namespace SciTech.Rpc.Server.Internal
{
    public interface IRpcServiceActivator
    {
        IReadOnlyList<string> GetPublishedServices(RpcObjectId objectId);

        TService? GetServiceImpl<TService>(IServiceProvider? serviceProvider, RpcObjectId id) where TService : class;
    }
}
