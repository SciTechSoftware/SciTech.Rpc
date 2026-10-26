#region Copyright notice and license
// Copyright (c) SciTech Software AB and contributors.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License.
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Internal
{
    /// <summary>
    /// The built-in server info operation, which all servers implement. Use
    /// <see cref="Client.RpcChannelExtensions.GetServerInfoAsync"/> to call it.
    /// </summary>
    /// <remarks>
    /// The interface is public so that proxies can be generated for it. Servers implement the operation directly, not
    /// as a published service, so it's not included among the discoverable services.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [RpcService(Namespace = "SciTech.Rpc", Name = "RpcService")]
    public interface IRpcServerInfoService
    {
        Task<RpcServerInfoResponse> GetServerInfoAsync(CancellationToken cancellationToken);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [DataContract]
    [Serializable]
    public sealed class RpcServerInfoResponse
    {
        public const string OperationName = "GetServerInfo";

        public const string ServiceName = "SciTech.Rpc.RpcService";

        [DataMember(Order = 1)]
        public RpcConnectionInfo? ConnectionInfo { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Internal serialization")]
        [DataMember(Order = 2)]
        public Dictionary<string, string>? Properties { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Internal serialization")]
        [DataMember(Order = 3)]
        public string[]? Services { get; set; }
    }
}
