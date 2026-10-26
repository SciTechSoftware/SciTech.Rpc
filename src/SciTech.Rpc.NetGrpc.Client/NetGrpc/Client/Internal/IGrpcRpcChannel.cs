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
