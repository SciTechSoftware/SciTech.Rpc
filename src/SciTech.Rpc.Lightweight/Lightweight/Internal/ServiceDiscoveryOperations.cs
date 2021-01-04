using SciTech.Rpc.Internal;
using SciTech.Rpc.Serialization;
using System;
using System.Collections.Generic;
using System.Text;

namespace SciTech.Rpc.Lightweight.Internal
{
    internal static class ServiceDiscoveryOperations
    {        
        /// <summary>
        /// Request: <see cref="RpcRequest"/>
        /// Response: <see cref="RpcPublishedSingletonsResponse"/>
        /// </summary>
        internal const string GetPublishedSingletons = "SciTech.Rpc.ServiceDiscovery.GetPublishedSingletons";

        /// <summary>
        /// Request: <see cref="RpcRequest"/>
        /// Response: <see cref="RpcConnectionInfoResponse"/>
        /// </summary>
        internal const string GetConnectionInfo = "SciTech.Rpc.ServiceDiscovery.GetConnectionInfo";

        internal static readonly IRpcSerializer DiscoverySerializer = new JsonRpcSerializer();
    }
}
