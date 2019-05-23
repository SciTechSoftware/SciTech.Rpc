using System;

namespace SciTech.Rpc.Server
{
    public interface IRpcServerEndPoint
    {
        string DisplayName { get; }

        string HostName { get; }

        RpcServerConnectionInfo GetConnectionInfo(RpcServerId serverId);
    }
}
