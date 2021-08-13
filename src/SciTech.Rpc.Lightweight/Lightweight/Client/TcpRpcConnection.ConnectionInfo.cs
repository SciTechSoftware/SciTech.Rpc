#region Copyright notice and license

// Copyright (c) 2019-2021, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License.
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//

#endregion Copyright notice and license

using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace SciTech.Rpc.Lightweight.Client
{
    public partial class TcpRpcConnection
    {
        public static RpcConnectionInfo CreateConnectionInfo(EndPoint endPoint, RpcServerId serverId = default)
        {
            return new RpcConnectionInfo(new Uri($"lightweight.tcp://{endPoint}", UriKind.Absolute), serverId);
        }

        public static bool TryParseConnectionInfo(string hostOrIpAddress, int port, RpcServerId serverId, [NotNullWhen(true)] out RpcConnectionInfo? connectionInfo)
        {
            if (IPAddress.TryParse(hostOrIpAddress, out var parsedIp))
            {
                string urlIpAddress = hostOrIpAddress;

                if (parsedIp.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    urlIpAddress = FormattableString.Invariant($"[{parsedIp}]");
                }
                connectionInfo = new RpcConnectionInfo(new Uri($"lightweight.tcp://{urlIpAddress}:{port}", UriKind.Absolute), serverId);

                return true;
            }

            if (!string.IsNullOrWhiteSpace(hostOrIpAddress))
            {
                if (Uri.TryCreate($"lightweight.tcp://{hostOrIpAddress}:{port}", UriKind.Absolute, out var hostUri))
                {
                    connectionInfo = new RpcConnectionInfo(hostUri, serverId);
                    return true;
                }
            }

            connectionInfo = null;
            return false;
        }

        public static bool TryParseConnectionInfo(string hostOrIpAddress, int port, [NotNullWhen(true)] out RpcConnectionInfo? connectionInfo)
        {
            return TryParseConnectionInfo(hostOrIpAddress, port, RpcServerId.Empty, out connectionInfo);
        }
    }
}