using System;
using System.Collections.Generic;
using System.Text;

namespace SciTech.Rpc.Lightweight.Internal
{
    public class RpcPublishedSingletonsResponse
    {
        public RpcServerConnectionInfo? ConnectionInfo { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Internal serialization")]
        public RpcPublishedSingleton[]? Services { get; set; }
    }

    public class RpcConnectionInfoResponse
    {
        public RpcServerConnectionInfo? ConnectionInfo { get; set; }
    }

    public class RpcPublishedSingleton
    {
        public string? ServiceName { get; set; }

        // Not implemented yet.
        //public string? SerializerName { get; set; }

        // Not implemented yet.
        //public int Version { get; set; }

    }
}
