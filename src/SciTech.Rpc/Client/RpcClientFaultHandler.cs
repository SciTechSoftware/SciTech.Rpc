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

using System;
using System.Collections.Generic;

namespace SciTech.Rpc.Client
{
    public sealed class RpcClientFaultHandler
    {
        private readonly Dictionary<string, IRpcClientExceptionConverter>? faultExceptionConverters;

        public RpcClientFaultHandler(IReadOnlyCollection<IRpcClientExceptionConverter> faultExceptionConverters)
        {
            if (faultExceptionConverters != null)
            {
                this.faultExceptionConverters = new Dictionary<string, IRpcClientExceptionConverter>(faultExceptionConverters.Count);
                foreach (var c in faultExceptionConverters)
                {
                    this.faultExceptionConverters.Add(c.FaultCode, c);
                }
            }
        }

        public bool TryGetFaultConverter(string faultCode, out IRpcClientExceptionConverter? faultInfo)
        {
            faultInfo = default;
            return this.faultExceptionConverters != null && this.faultExceptionConverters.TryGetValue(faultCode, out faultInfo);
        }
    }
}
