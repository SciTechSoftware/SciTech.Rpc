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

using SciTech.Rpc.Internal;
using System;

namespace SciTech.Rpc.Client.Internal
{
    public abstract class RpcProxyMethod
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="faultHandler">Optional fault handler that will be used when an operation returns an <see cref="RpcError"/>.</param>
        protected RpcProxyMethod(IRpcSerializer? serializerOverride, RpcClientFaultHandler? faultHandler)
        {
            this.SerializerOverride = serializerOverride;
            this.FaultHandler = faultHandler;
        }

        public RpcClientFaultHandler? FaultHandler { get; }

        public IRpcSerializer? SerializerOverride { get; }
    }
}
