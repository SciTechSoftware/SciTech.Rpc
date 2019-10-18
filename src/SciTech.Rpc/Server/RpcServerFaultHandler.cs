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
using System.Linq;

namespace SciTech.Rpc.Server
{
    public sealed class RpcServerFaultHandler
    {
        public static readonly RpcServerFaultHandler Default = new RpcServerFaultHandler();

        private readonly HashSet<string>? declaredFaults;

        private readonly Dictionary<Type, List<IRpcServerExceptionConverter>>? faultGenerators;

        internal RpcServerFaultHandler(IEnumerable<IRpcServerExceptionConverter> exceptionConverters)
        {
            if (exceptionConverters != null)
            {
                this.faultGenerators = new Dictionary<Type, List<IRpcServerExceptionConverter>>();
                this.declaredFaults = new HashSet<string>();

                foreach (var exceptionConverter in exceptionConverters)
                {
                    if (!this.declaredFaults.Add(exceptionConverter.FaultCode))
                    {
                        throw new RpcDefinitionException($"Fault contract for fault code '{exceptionConverter.FaultCode}' has already been added.");
                    }

                    if (!this.faultGenerators.TryGetValue(exceptionConverter.ExceptionType, out var convertersList))
                    {
                        convertersList = new List<IRpcServerExceptionConverter>();
                        this.faultGenerators.Add(exceptionConverter.ExceptionType, convertersList);
                    }

                    convertersList.Add(exceptionConverter);
                }
            }
        }

        private RpcServerFaultHandler()
        {
        }

        public bool IsFaultDeclared(string faultCode) => this.declaredFaults?.Contains(faultCode) == true;

        public bool TryGetExceptionConverter(Exception e, out IReadOnlyList<IRpcServerExceptionConverter>? converters)
        {
            if (e != null && this.faultGenerators != null && this.faultGenerators.TryGetValue(e.GetType(), out var convertersList))
            {
                converters = convertersList;
                return true;
            }

            converters = default;
            return false;
        }
    }
}
