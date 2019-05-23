using Grpc.Core;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using System;
using System.Threading;

namespace SciTech.Rpc.Grpc.Server.Internal
{
    internal class GrpcCallContext : IRpcCallContext, IRpcServerCallMetadata
    {
        private readonly ServerCallContext callContext;

        internal GrpcCallContext(ServerCallContext callContext)
        {
            this.callContext = callContext;
        }

        public CancellationToken CancellationToken => this.callContext.CancellationToken;

        public IRpcServerCallMetadata RequestHeaders => this;

        public string? GetValue(string key)
        {
            var metadata = this.callContext.RequestHeaders;
            for (int i = 0; i < metadata.Count; i++)
            {
                var entry = metadata[i];
                if (entry.Key == key)
                {
                    return entry.Value;
                }
            }

            return null;
        }
    }
}
