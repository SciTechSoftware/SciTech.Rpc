using SciTech.Rpc.Client;
using SciTech.Rpc.Serialization;
using System;
using GrpcCore = Grpc.Core;

#if FEATURE_NET_GRPC
namespace SciTech.Rpc.NetGrpc.Client.Internal
#else
namespace SciTech.Rpc.Grpc.Client.Internal
#endif
{
    internal interface IGrpcServerConnection : IRpcServerConnection
    {
        GrpcCore.CallInvoker? CallInvoker { get; }

        IRpcSerializer Serializer { get; }
    }
}
