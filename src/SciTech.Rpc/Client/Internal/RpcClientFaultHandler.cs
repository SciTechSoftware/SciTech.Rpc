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
using SciTech.Rpc.Serialization;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SciTech.Rpc.Client.Internal
{

    public interface IRpcFaultExceptionFactory
    {
        /// <summary>
        /// Gets the code of the fault that this converter can handle.
        /// </summary>
        string FaultCode { get; }

        /// <summary>
        /// Gets the type of the fault details used by this converter, or <c>null</c> if 
        /// no fault details are needed.
        /// </summary>
        Type? FaultDetailsType { get; }

        /// <summary>
        /// Creates a custom exception based on <see cref="FaultCode"/>, <paramref name="message"/>
        /// and the optional <paramref name="details"/>.
        /// </summary>
        /// <param name="message">Message text as provided by the RPC fault.</param>
        /// <param name="details">Optional details. Will always be provided if <see cref="FaultDetailsType"/> is not <c>null</c>.</param>
        /// <returns></returns>
        RpcFaultException CreateFaultException(string message, object? details);
    }

    internal class RpcFaultExceptionFactory : IRpcFaultExceptionFactory
    {
        public RpcFaultExceptionFactory(string faultCode)
        {
            this.FaultCode = faultCode;
        }

        public string FaultCode { get; }

        public Type? FaultDetailsType => null;

        public RpcFaultException CreateFaultException(string message, object? details)
        {
            return new RpcFaultException(this.FaultCode, message);
        }
    }

    internal class RpcFaultExceptionFactory<TFault> : IRpcFaultExceptionFactory where TFault : class
    {
        public RpcFaultExceptionFactory(string faultCode)
        {
            this.FaultCode = faultCode;
        }

        public string FaultCode { get; }

        public Type? FaultDetailsType => typeof(TFault);

        public RpcFaultException CreateFaultException(string message, object? details)
        {
            return new RpcFaultException<TFault>(this.FaultCode, message, (TFault)details!);
        }
    }


    public sealed class RpcClientFaultHandler
    {
        public static readonly RpcClientFaultHandler Empty = new RpcClientFaultHandler();

        public RpcClientFaultHandler? baseHandler;

        private readonly ImmutableArray<IRpcClientExceptionConverter> customExceptionConverters;

        private readonly ImmutableArray<IRpcFaultExceptionFactory> faultExceptionFactories;

        private RpcClientFaultHandler()
        {
            this.customExceptionConverters = ImmutableArray<IRpcClientExceptionConverter>.Empty;
            this.faultExceptionFactories = ImmutableArray<IRpcFaultExceptionFactory>.Empty;
        }

        public RpcClientFaultHandler(RpcClientFaultHandler? baseHandler, IReadOnlyCollection<IRpcFaultExceptionFactory> faultExceptionFactories, IReadOnlyCollection<IRpcClientExceptionConverter> faultExceptionConverters)
        {
            this.baseHandler = baseHandler;
            this.faultExceptionFactories = faultExceptionFactories.ToImmutableArray();
            this.customExceptionConverters = faultExceptionConverters.ToImmutableArray();
        }

        internal RpcFaultException CreateFaultException(RpcError error, IRpcSerializer serializer)
        {
            RpcFaultException? faultException = null;
            bool matched = false;
            foreach( var factory in this.faultExceptionFactories )
            {
                if( factory.FaultCode == error.ErrorCode)
                {
                    matched = true;
                    if (factory.FaultDetailsType != null)
                    {
                        if (error.ErrorDetails != null)
                        {
                            object? details = serializer.Deserialize(error.ErrorDetails, factory.FaultDetailsType);

                            faultException = factory.CreateFaultException(error.Message ?? "", details);
                        }
                    }
                    else
                    {
                        faultException = factory.CreateFaultException(error.Message ?? "", null);
                    }

                    break;
                }
            }

            if( !matched && baseHandler != null )
            {
                faultException = baseHandler.CreateFaultException(error, serializer);
            }
            
            return faultException ?? new RpcFaultException(error.ErrorCode ?? "", error.Message ?? "");
        }

        internal Exception? TryConvertException(RpcFaultException faultException)
        {
            foreach (var converter in this.customExceptionConverters)
            {
                var exception = converter.TryCreateException(faultException);
                if (exception != null)
                {
                    return exception;
                }
            }

            return baseHandler?.TryConvertException(faultException);
        }

        //internal Exception CreateException(RpcError error, IRpcSerializer serializer )
        //{
        //    var faultException = this.CreateFaultException(error, serializer);
            
        //    foreach( var converter in this.customExceptionConverters)
        //    {
        //        var exception = converter.TryCreateException(faultException);
        //        if( exception != null )
        //        {
        //            return exception;
        //        }
        //    }
        //}

        //public bool TryGetCustomFaultConverter(string faultCode, out IRpcClientExceptionConverter? faultInfo)
        //{
        //    faultInfo = default;
        //    return this.faultExceptionConverters != null && this.faultExceptionConverters.TryGetValue(faultCode, out faultInfo);
        //}
        
        //public bool TryGetDeclaredConverter(string faultCode, out IRpcClientExceptionConverter? faultInfo)
        //{
        //    faultInfo = default;
        //    return this.faultExceptionConverters != null && this.faultExceptionConverters.TryGetValue(faultCode, out faultInfo);
        //}
    }
}
