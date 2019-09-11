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
    public class LightweightMethodDef : RpcProxyMethod
    {
        public LightweightMethodDef(
            RpcMethodType methodType,
            string operationName,
            Type requestType,
            Type responseType,
            IRpcSerializer? serializerOverride,
            RpcClientFaultHandler? faultHandler)
            : base(serializerOverride, faultHandler)
        {
            this.MethodType = methodType;
            this.OperationName = operationName;
            this.RequestType = requestType;
            this.ResponseType = responseType;
        }

        public RpcMethodType MethodType { get; }

        public string OperationName { get; }

        public Type RequestType { get; }

        public Type ResponseType { get; }
    }
}
