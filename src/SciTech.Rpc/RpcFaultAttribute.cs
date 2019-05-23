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

namespace SciTech.Rpc
{
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true)]
    public class RpcFaultAttribute : Attribute
    {
        public RpcFaultAttribute(Type faultType)
        {
            this.FaultType = faultType;
            this.FaultCode = RpcBuilderUtil.RetrieveFaultCode(faultType);
        }

        public RpcFaultAttribute(string faultCode)
        {
            this.FaultCode = faultCode;
        }

        public string FaultCode { get; set; }

        public Type? FaultType { get; }
    }
}
