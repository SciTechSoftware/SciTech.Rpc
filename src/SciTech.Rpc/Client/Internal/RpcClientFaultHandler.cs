#region Copyright notice and license
// Copyright (c) SciTech Software AB and contributors.
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
using SciTech.Rpc.Server;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;

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

        private readonly RpcClientFaultHandler? baseHandler;

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

        /// <summary>
        /// Creates a fault handler for the exception converters registered in the client options. A converter that also
        /// implements <see cref="IRpcServerExceptionConverter"/>, e.g. <see cref="RpcExceptionConverter{TException, TFault}"/>,
        /// declares its fault, so that the fault details can be deserialized.
        /// </summary>
        internal static RpcClientFaultHandler CreateRegisteredFaultHandler(IEnumerable<IRpcClientExceptionConverter> exceptionConverters)
        {
            var factories = new List<IRpcFaultExceptionFactory>();
            var converters = new List<IRpcClientExceptionConverter>();
            foreach (var converter in exceptionConverters)
            {
                converters.Add(converter);
                if (TryCreateFaultExceptionFactory(converter) is IRpcFaultExceptionFactory factory)
                {
                    factories.Add(factory);
                }
            }

            return converters.Count > 0 ? new RpcClientFaultHandler(null, factories, converters) : Empty;
        }

        internal static IRpcFaultExceptionFactory CreateFaultExceptionFactory(string faultCode, Type? faultType)
        {
            ConstructorInfo factoryCtor;
            if (faultType != null)
            {
                var factoryType = typeof(RpcFaultExceptionFactory<>).MakeGenericType(faultType);
                factoryCtor = factoryType.GetConstructor(new Type[] { typeof(string) })
                    ?? throw new NotImplementedException($"{factoryType.Name} constructor not found.");
            }
            else
            {
                factoryCtor = typeof(RpcFaultExceptionFactory).GetConstructor(new Type[] { typeof(string) })
                    ?? throw new NotImplementedException($"{nameof(RpcFaultExceptionFactory)} constructor not found.");
            }

            return (IRpcFaultExceptionFactory)factoryCtor.Invoke(new object[] { faultCode });
        }

        /// <summary>
        /// Creates the factory of the fault that <paramref name="converter"/> converts, if the converter declares its fault
        /// code (by also implementing <see cref="IRpcServerExceptionConverter"/>).
        /// </summary>
        internal static IRpcFaultExceptionFactory? TryCreateFaultExceptionFactory(IRpcClientExceptionConverter converter)
            => converter is IRpcServerExceptionConverter declaringConverter && !string.IsNullOrEmpty(declaringConverter.FaultCode)
                ? CreateFaultExceptionFactory(declaringConverter.FaultCode, declaringConverter.FaultDetailsType)
                : null;

        internal RpcFaultException CreateFaultException(RpcError error, IRpcSerializer serializer)
            => this.TryCreateFaultException(error, serializer) ?? new RpcFaultException(error.ErrorCode ?? "", error.Message ?? "");

        /// <summary>
        /// Creates the fault exception of <paramref name="error"/>, if its fault is declared by this handler.
        /// </summary>
        /// <returns>The fault exception, or <c>null</c> if the fault is not declared.</returns>
        internal RpcFaultException? TryCreateFaultException(RpcError error, IRpcSerializer serializer)
        {
            foreach (var factory in this.faultExceptionFactories)
            {
                if (factory.FaultCode == error.ErrorCode)
                {
                    if (factory.FaultDetailsType != null)
                    {
                        if (error.ErrorDetails != null)
                        {
                            object? details = serializer.Deserialize(error.ErrorDetails, factory.FaultDetailsType);

                            return factory.CreateFaultException(error.Message ?? "", details);
                        }

                        // A declared fault without details.
                        return new RpcFaultException(error.ErrorCode ?? "", error.Message ?? "");
                    }

                    return factory.CreateFaultException(error.Message ?? "", null);
                }
            }

            return this.baseHandler?.TryCreateFaultException(error, serializer);
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
