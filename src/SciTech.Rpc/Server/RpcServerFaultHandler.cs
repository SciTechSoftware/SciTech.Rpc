#region Copyright notice and license

// Copyright (c) 2019, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License.
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//

#endregion Copyright notice and license

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SciTech.Rpc.Server
{
    public sealed class RpcServerFaultHandler
    {
        internal static readonly RpcServerFaultHandler Default = new RpcServerFaultHandler();

        private readonly RpcServerFaultHandler? baseHandler;

        private ImmutableArray<IRpcServerExceptionConverter> converters = ImmutableArray<IRpcServerExceptionConverter>.Empty;

        private ImmutableArray<FaultMapping> faultCodeToDetails = ImmutableArray<FaultMapping>.Empty;

        internal RpcServerFaultHandler(RpcServerFaultHandler? baseHandler, IEnumerable<IRpcServerExceptionConverter> exceptionConverters, IEnumerable<FaultMapping>? faultCodeToDetails)
        {
            this.baseHandler = baseHandler;
            if (exceptionConverters != null)
            {
                HashSet<string> declaredFaults = new HashSet<string>();

                var convertersList = ImmutableArray.CreateBuilder<IRpcServerExceptionConverter>();
                foreach (var exceptionConverter in exceptionConverters)
                {
                    if (!declaredFaults.Add(exceptionConverter.FaultCode))
                    {
                        throw new RpcDefinitionException($"Fault converter for fault code '{exceptionConverter.FaultCode}' has already been added.");
                    }
                    //if (this.declaredFaults.TryGetValue(exceptionConverter.FaultCode, out var currConverter))
                    //{
                    //    if (!Equals(exceptionConverter.FaultDetailsType, currConverter.FaultDetailsType))
                    //    {
                    //        throw new RpcDefinitionException($"Fault contract for fault code '{exceptionConverter.FaultCode}' has already been added.");
                    //    }
                    //}

                    convertersList.Add(exceptionConverter);
                }

                this.converters = convertersList.ToImmutable();
            }

            if (faultCodeToDetails != null)
            {
                var mappingsList = ImmutableArray.CreateBuilder<FaultMapping>();
                foreach (var mapping in faultCodeToDetails)
                {
                    // TODO: Validate
                    mappingsList.Add(mapping);
                }

                this.faultCodeToDetails = mappingsList.ToImmutable();
            }
        }

        private RpcServerFaultHandler()
        {
        }

        public bool IsFaultDeclared(string faultCode, Type? detailsType)
        {
            foreach (var pair in this.faultCodeToDetails)
            {
                if (pair.FaultCode == faultCode)
                {
                    // It's declared
                    if (!Equals(pair.DetailsType, detailsType))
                    {
                        // But with another details type.
                        return false;
                    }

                    return true;
                }
            }

            return this.baseHandler?.IsFaultDeclared(faultCode, detailsType) ?? false;
        }

        public RpcFaultException? TryCreateFaultException(Exception exception)
        {
            foreach (var converter in this.converters)
            {
                if (converter.CreateFault(exception) is RpcFaultException faultException)
                {
                    return faultException;
                }
            }

            return this.baseHandler?.TryCreateFaultException(exception);
        }
    }

    public readonly struct FaultMapping
    {
        public readonly string FaultCode;
        public readonly Type DetailsType;

        internal FaultMapping(string faultCode, Type detailsType)
        {
            this.FaultCode = faultCode;
            this.DetailsType = detailsType;
        }
    }


}