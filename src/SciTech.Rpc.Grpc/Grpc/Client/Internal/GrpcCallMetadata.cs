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
    public class GrpcCallMetadata : IRpcClientCallMetadata
    {
        private readonly GrpcCore.Metadata metadata;

        internal GrpcCallMetadata(GrpcCore.Metadata metadata)
        {
            this.metadata = metadata;
        }

        public void AddValue(string key, string value)
        {
            this.metadata.Add(key, value);
        }
    }
}
