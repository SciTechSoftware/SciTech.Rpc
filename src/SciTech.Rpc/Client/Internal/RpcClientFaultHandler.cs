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
using System.Collections.Immutable;

namespace SciTech.Rpc.Client.Internal
{
    public sealed class RpcClientFaultHandler
    {
        public static readonly RpcClientFaultHandler Empty = new RpcClientFaultHandler();

        private readonly IReadOnlyDictionary<string, IRpcClientExceptionConverter> faultExceptionConverters;

        private RpcClientFaultHandler()
        {
            this.faultExceptionConverters = ImmutableDictionary<string, IRpcClientExceptionConverter>.Empty;
        }

        public RpcClientFaultHandler(RpcClientFaultHandler? baseHandler, IReadOnlyCollection<IRpcClientExceptionConverter> faultExceptionConverters)
        {
            if (faultExceptionConverters?.Count > 0)
            {
                Dictionary<string, IRpcClientExceptionConverter> combinedConverters;

                if (baseHandler != null)
                {
                    combinedConverters = new Dictionary<string, IRpcClientExceptionConverter>(baseHandler.faultExceptionConverters.Count);
                    foreach( var pair in baseHandler.faultExceptionConverters)
                    {
                        combinedConverters.Add(pair.Key, pair.Value);
                    }
                } else
                {
                    combinedConverters = new Dictionary<string, IRpcClientExceptionConverter>(faultExceptionConverters.Count);
                }

                foreach (var c in faultExceptionConverters)
                {
                    combinedConverters[c.FaultCode] = c;
                }

                this.faultExceptionConverters = combinedConverters;
            } else
            {
                this.faultExceptionConverters = (baseHandler ?? Empty).faultExceptionConverters;
            }
        }

        public bool TryGetFaultConverter(string faultCode, out IRpcClientExceptionConverter? faultInfo)
        {
            faultInfo = default;
            return this.faultExceptionConverters != null && this.faultExceptionConverters.TryGetValue(faultCode, out faultInfo);
        }
    }
}
