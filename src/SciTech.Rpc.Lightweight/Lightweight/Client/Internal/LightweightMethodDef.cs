﻿#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using SciTech.Rpc.Client;
using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Internal;
using System;

namespace SciTech.Rpc.Lightweight.Client.Internal
{
    public abstract class LightweightMethodDef : RpcProxyMethod
    {
        public LightweightMethodDef(
            RpcMethodType methodType,
            string operationName,
            IRpcSerializer? serializerOverride,
            RpcClientFaultHandler? faultHandler)
            : base(serializerOverride, faultHandler)
        {
            this.MethodType = methodType;
            this.OperationName = operationName;
        }

        public RpcMethodType MethodType { get; }

        public string OperationName { get; }
    }
    
    public class LightweightMethodDef<TRequest,TResponse> : LightweightMethodDef
    {
        public LightweightMethodDef(
            RpcMethodType methodType,
            string operationName,
            IRpcSerializer? serializerOverride,
            RpcClientFaultHandler? faultHandler)
            : base(methodType, operationName, serializerOverride, faultHandler)
        {
        }

        protected internal override Type RequestType => typeof(TRequest);

        protected internal override Type ResponseType => typeof(TResponse);
    }
}
