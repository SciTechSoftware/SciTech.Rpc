using SciTech.Rpc.Internal;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace SciTech.Rpc.Lightweight.Internal
{
    [DataContract]
    [Serializable]
    public sealed class RpcDiscoveryRequest : IObjectRequest
    {
        public RpcDiscoveryRequest()
        {

        }

        public RpcDiscoveryRequest(Guid clientId)
        {
            this.ClientId = clientId;
        }

        [DataMember(Order=1)]
        public Guid ClientId { get; set; }

        RpcObjectId IObjectRequest.Id => RpcObjectId.Empty;

        void IObjectRequest.Clear()
        {
            throw new NotImplementedException();
        }
    }

    [DataContract]
    [Serializable]
    public class RpcPublishedSingletonsResponse
    {
        [DataMember(Order = 1)]
        public Guid ClientId { get; set; }

        [DataMember(Order = 2)]
        public RpcServerConnectionInfo? ConnectionInfo { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Internal serialization")]
        [DataMember(Order = 3)]
        public RpcPublishedSingleton[]? Services { get; set; }
    }

    [DataContract]
    [Serializable]
    public class RpcConnectionInfoResponse
    {
        [DataMember(Order = 1)]
        public Guid ClientId { get; set; }

        [DataMember(Order = 2)]
        public RpcServerConnectionInfo? ConnectionInfo { get; set; }
    }

    [DataContract]
    [Serializable]
    public class RpcPublishedSingleton
    {
        [DataMember(Order = 1)]
        public string? Name { get; set; }

        // Not implemented yet.
        //public string? SerializerName { get; set; }

        // Not implemented yet.
        //public int Version { get; set; }

    }
}
