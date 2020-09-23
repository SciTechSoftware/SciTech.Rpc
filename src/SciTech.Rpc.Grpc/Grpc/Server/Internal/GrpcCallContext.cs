using Grpc.Core;
using SciTech.Collections;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using System;
using System.Collections.Immutable;
using System.Threading;

namespace SciTech.Rpc.Grpc.Server.Internal
{
    internal class GrpcCallContext : IRpcCallContextWithCancellation
    {
        private readonly ServerCallContext callContext;

        internal GrpcCallContext(ServerCallContext callContext)
        {
            this.callContext = callContext;
        }

        public CancellationToken CancellationToken => this.callContext.CancellationToken;

        public string? GetHeaderString(string key)
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


        public ImmutableArray<byte> GetBinaryHeader(string key)
        {
            var metadata = this.callContext.RequestHeaders;
            for (int i = 0; i < metadata.Count; i++)
            {
                var entry = metadata[i];
                if (entry.Key == key)
                {
                    return entry.ValueBytes.ToImmutableArray();
                }
            }

            return default;
        }
    }
}
