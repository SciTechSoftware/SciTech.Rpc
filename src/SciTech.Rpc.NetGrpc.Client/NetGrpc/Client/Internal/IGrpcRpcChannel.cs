using SciTech.Rpc.Client;
using SciTech.Rpc.Serialization;
using System;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.NetGrpc.Client.Internal
{
    internal interface IGrpcRpcChannel : IRpcChannel
    {
        GrpcCore.CallInvoker? CallInvoker { get; }

        IRpcSerializer Serializer { get; }
    }
}
