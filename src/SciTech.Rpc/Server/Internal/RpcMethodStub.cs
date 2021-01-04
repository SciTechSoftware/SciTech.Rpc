#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using SciTech.Rpc.Serialization;
using System;

namespace SciTech.Rpc.Server.Internal
{
    public interface IRpcMethodStub
    {
        RpcServerFaultHandler? FaultHandler { get; }

        IRpcSerializer Serializer { get; }
    }

    public class RpcMethodStub : IRpcMethodStub
    {
        public RpcMethodStub(IRpcSerializer serializer, RpcServerFaultHandler? faultHandler)
        {
            this.Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            this.FaultHandler = faultHandler;
        }

        public RpcServerFaultHandler? FaultHandler { get; }

        public IRpcSerializer Serializer { get; }
    }
}
