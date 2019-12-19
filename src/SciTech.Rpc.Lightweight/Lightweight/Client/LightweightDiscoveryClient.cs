using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Client
{
    public class LightweightDiscoveryClient
    {
        public IImmutableList<DiscoveredService> DiscoveredServices { get; private set; } = ImmutableList.Create<DiscoveredService>();

        public event EventHandler<DiscoveredServiceEventArgs>? ServiceDiscovered;

        public Task<IImmutableList<DiscoveredService>> FindAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult((IImmutableList<DiscoveredService>)ImmutableList<DiscoveredService>.Empty);
        }
    }

    public sealed class DiscoveredServiceEventArgs : EventArgs
    {
        internal DiscoveredServiceEventArgs(DiscoveredService discoveredService)
        {
            this.DiscoveredService = discoveredService;
        }

        public DiscoveredService DiscoveredService { get; }
    }

    public sealed class DiscoveredService
    {
        internal DiscoveredService(RpcServerConnectionInfo connectionInfo, string service, int version)
        {
            this.ConnectionInfo = connectionInfo;
            this.Service = service;
            this.Version = version;
        }

        public RpcServerConnectionInfo ConnectionInfo { get; }

        public string Service { get; }

        public int Version { get; }
    }
}
