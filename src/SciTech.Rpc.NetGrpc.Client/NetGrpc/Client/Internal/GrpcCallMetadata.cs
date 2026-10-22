using SciTech.Rpc.Client;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.NetGrpc.Client.Internal
{
    public class GrpcCallMetadata : IRpcRequestContext
    {
        private readonly GrpcCore.Metadata metadata;

        internal GrpcCallMetadata(GrpcCore.Metadata metadata)
        {
            this.metadata = metadata;
        }

        /// <summary>
        /// Cancellation by interceptors not (yet?) implemented.
        /// </summary>
        public CancellationToken CancellationToken => default;

        public void AddBinaryHeader(string key, IReadOnlyList<byte> value)
        {
            this.metadata.Add(key, value.ToArray());
        }

        public void AddHeader(string key, string value)
        {
            this.metadata.Add(key, value);
        }

        public ImmutableArray<byte> GetBinaryHeader(string key)
        {
            for (int i = 0; i < this.metadata.Count; i++)
            {
                var entry = metadata[i];
                if (entry.Key == key)
                {
                    return entry.ValueBytes.ToImmutableArray();
                }
            }

            return default;
        }

        public string? GetHeaderString(string key)
        {
            for (int i = 0; i < this.metadata.Count; i++)
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
