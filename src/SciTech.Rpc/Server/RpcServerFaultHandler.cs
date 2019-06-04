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

        internal RpcServerFaultHandler(params IEnumerable<IRpcServerExceptionConverter>?[] errorGenerators)
        {
        }

        internal RpcServerFaultHandler(IEnumerable<IRpcServerExceptionConverter> errorGenerators)//, IRpcSerializer serializer)
        {
            //this.Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));

            if (errorGenerators != null)
            {
                this.faultGenerators = new Dictionary<Type, List<IRpcServerExceptionConverter>>();
                this.declaredFaults = new HashSet<string>();

                foreach (var errorGenerator in errorGenerators)
                {
                    if (!this.declaredFaults.Add(errorGenerator.FaultCode))
                    {
                        throw new RpcDefinitionException($"Fault contract for fault code '{errorGenerator.FaultCode}' has already been added.");
                    }

                    if (!this.faultGenerators.TryGetValue(errorGenerator.ExceptionType, out var convertersList))
                    {
                        convertersList = new List<IRpcServerExceptionConverter>();
                        this.faultGenerators.Add(errorGenerator.ExceptionType, convertersList);
                    }

                    convertersList.Add(errorGenerator);
                }
            }
        }

        private RpcServerFaultHandler()
        {

        }

        public bool IsFaultDeclared(string faultCode) => this.declaredFaults?.Contains(faultCode) == true;
        //internal IRpcSerializer? Serializer { get; }

        public bool TryGetExceptionConverter(Exception e, out IReadOnlyList<IRpcServerExceptionConverter>? converters)
        {
            if (this.faultGenerators != null && this.faultGenerators.TryGetValue(e.GetType(), out var convertersList))
            {
                converters = convertersList;
                return true;
            }

            converters = default;
            return false;
        }
        //public ConvertedFault HandleException(Exception e, IRpcSerializer serializer)
        //{
        //    if (this.faultGenerators != null && this.faultGenerators.TryGetValue(e.GetType(), out var faultGenerator))
        //    {
        //        return faultGenerator.CreateFault(e);
        //    }
        //    return null;
        //}
    }
}
