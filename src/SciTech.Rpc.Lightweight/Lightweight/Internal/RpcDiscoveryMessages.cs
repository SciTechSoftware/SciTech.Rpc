using SciTech.Rpc.Internal;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SciTech.Rpc.Lightweight.Internal
{
    // The discovery messages, see the Lightweight protocol specification (docs/lightweight-protocol.md, section 7.4).
    // They're serialized by ServiceDiscoveryOperations.DiscoverySerializer, so the property names are the JSON names.

    [DataContract]
    [Serializable]
    public sealed class RpcFindServersRequest : IObjectRequest
    {
        public RpcFindServersRequest()
        {
        }

        public RpcFindServersRequest(Guid clientId, string[]? services)
        {
            this.ClientId = clientId;
            this.Services = services;
        }

        [DataMember(Order = 1)]
        public Guid ClientId { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Internal serialization")]
        [DataMember(Order = 2)]
        public string[]? Services { get; set; }

        RpcObjectId IObjectRequest.Id => RpcObjectId.Empty;

        void IObjectRequest.Clear()
        {
            this.ClientId = Guid.Empty;
            this.Services = null;
        }
    }

    [DataContract]
    [Serializable]
    public sealed class RpcFindServersResponse
    {
        [DataMember(Order = 1)]
        public Guid ClientId { get; set; }

        [DataMember(Order = 2)]
        public RpcConnectionInfo? ConnectionInfo { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Internal serialization")]
        [DataMember(Order = 3)]
        public Dictionary<string, string>? Properties { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Internal serialization")]
        [DataMember(Order = 4)]
        public int[]? Matches { get; set; }

        [DataMember(Order = 5)]
        public int ServiceCount { get; set; }

        [DataMember(Order = 6)]
        public string? ServicesHash { get; set; }
    }

    [DataContract]
    [Serializable]
    public sealed class RpcGetServicesRequest : IObjectRequest
    {
        public RpcGetServicesRequest()
        {
        }

        public RpcGetServicesRequest(Guid clientId, Guid serverId, int offset)
        {
            this.ClientId = clientId;
            this.ServerId = serverId;
            this.Offset = offset;
        }

        [DataMember(Order = 1)]
        public Guid ClientId { get; set; }

        [DataMember(Order = 2)]
        public Guid ServerId { get; set; }

        [DataMember(Order = 3)]
        public int Offset { get; set; }

        RpcObjectId IObjectRequest.Id => RpcObjectId.Empty;

        void IObjectRequest.Clear()
        {
            this.ClientId = Guid.Empty;
            this.ServerId = Guid.Empty;
            this.Offset = 0;
        }
    }

    [DataContract]
    [Serializable]
    public sealed class RpcGetServicesResponse
    {
        [DataMember(Order = 1)]
        public Guid ClientId { get; set; }

        [DataMember(Order = 2)]
        public Guid ServerId { get; set; }

        [DataMember(Order = 3)]
        public int ServiceCount { get; set; }

        [DataMember(Order = 4)]
        public string? ServicesHash { get; set; }

        [DataMember(Order = 5)]
        public int Offset { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Internal serialization")]
        [DataMember(Order = 6)]
        public string[]? Services { get; set; }
    }
}
