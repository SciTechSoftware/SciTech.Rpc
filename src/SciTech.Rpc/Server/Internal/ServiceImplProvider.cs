using System;
using System.Collections.Generic;
using System.Linq;

namespace SciTech.Rpc.Server.Internal
{
    public interface IRpcServiceActivator
    {
        IReadOnlyList<string> GetPublishedServices(RpcObjectId objectId);

        ActivatedService<TService>? GetActivatedService<TService>(IServiceProvider? serviceProvider, RpcObjectId id) where TService : class;
    }

#pragma warning disable CA1815 // Override equals and operator equals on value types
    public struct ActivatedService<TService> where TService : class
#pragma warning restore CA1815 // Override equals and operator equals on value types
    {
        public ActivatedService(TService service, bool shouldDispose)
        {
            this.Service = service;
            this.ShouldDispose = shouldDispose;
        }

        public TService Service { get; }

        public bool ShouldDispose { get; }
    }
}
