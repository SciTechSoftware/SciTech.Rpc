﻿using System;
using SciTech.Rpc.Client;

namespace SciTech.Rpc
{
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true)]
    public class RpcFaultConverterAttribute : Attribute
    {
        public RpcFaultConverterAttribute(Type converterType)
        {
            this.ConverterType = converterType;
        }


        /// <summary>
        /// Defines a conversion between an untyped RPC fault and the specified exception type. The 
        /// conversion is available for all RPC faults with a <see cref="RpcFaultException.FaultCode"/>
        /// that matches <paramref name="faultCode"/>.
        /// <para><b>NOTE.</b> The exception type must have a public constructor with the signature 
        /// <c>Exception(string message)</c>.
        /// </para>
        /// </summary>
        /// <param name="faultCode">RPC fault code to which this conversion applies.</param>
        /// <param name="exceptionType">The associated exception type.</param>
        public RpcFaultConverterAttribute(string faultCode, Type exceptionType)
        {
            this.FaultCode = faultCode;
            this.ExceptionType = exceptionType;
        }

        //public RpcFaultAttribute(Type faultType)
        //{
        //    this.FaultType = faultType;
        //    this.FaultCode = RpcBuilderUtil.RetrieveFaultCode(faultType);
        //}

        /// <summary>
        /// Gets or sets the <see cref="RpcFaultException.FaultCode"/>. It is retrieved from the <see cref="FaultType"/>
        /// but can be explicitly assigned if necessary.
        /// </summary>
        public string? FaultCode { get; set; }

        /// <summary>
        /// Gets the type of the optional customer converter to use. If this property is <c>null</c> then <see cref="FaultCode"/> and <see cref="ExceptionType"/>
        /// must be initialized. 
        /// <para>The custom converter type must implement
        /// <see cref="IRpcClientExceptionConverter"/> and have a default constructor or include a public static field or property 
        /// named "Default" with the type <see cref="IRpcClientExceptionConverter"/>.
        /// </para>
        /// </summary>
        public Type? ConverterType { get; }

        public Type? ExceptionType { get; }
    }
}