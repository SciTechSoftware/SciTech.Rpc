using SciTech.Rpc.Client;
using System;
using System.Linq;
using GrpcCore = Grpc.Core;

#if FEATURE_NET_GRPC
namespace SciTech.Rpc.NetGrpc.Client.Internal
#else
namespace SciTech.Rpc.Grpc.Client.Internal
#endif
{
    public class GrpcCallMetadata : IRpcClientCallContext
    {
        private readonly GrpcCore.Metadata metadata;

        internal GrpcCallMetadata(GrpcCore.Metadata metadata)
        {
            this.metadata = metadata;
        }

        public void AddHeader(string key, string value)
        {
            this.metadata.Add(key, value);
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
