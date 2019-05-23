using SciTech.Rpc.Client;
using System;
using System.Linq;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.Grpc.Client.Internal
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
