#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using SciTech.Rpc.Client;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Server;
using System;

namespace SciTech.Rpc
{
    /// <summary>
    /// Associates a fault details type with an RPC service operation. With the help of this attribute, a typed RPC fault (<see cref="RpcFaultException{TFault}"/>)
    /// can be propagated to the client side. 
    /// </summary>
    /// <remarks>
    /// To allow custom exceptions to be used by RPC service operations, use the <see cref="RpcFaultConverterAttribute"/>, or implement
    /// a custom exception converter using <see cref="IRpcClientExceptionConverter"/> and <see cref="IRpcServerExceptionConverter"/>.
    /// </remarks>
    /// <example>
    /// <code lang="C#">
    /// public class MathFault
    /// {
    ///   // TODO: Add additional details.
    /// }
    ///
    /// public interface IMathService
    /// {
    ///   [RpcFault(typeof(MathFault))]
    ///   int Divide(int a, int b);
    /// }
    /// 
    /// IMathService mathService = ...;
    /// try
    /// {
    ///   mathService.Divide(5, 0);
    /// }
    /// catch (RpcFaultException&lt;MathFault&gt;)
    /// {
    ///   Console.WriteLine("Math error occurred");
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true)]
    public sealed class RpcFaultAttribute : Attribute
    {
        public RpcFaultAttribute(Type faultType)
        {
            this.FaultType = faultType;
            this.FaultCode = RpcBuilderUtil.RetrieveFaultCode(faultType);
        }

        /// <summary>
        /// Gets or sets the <see cref="RpcFaultException.FaultCode"/>. It is retrieved from the <see cref="FaultType"/>
        /// but can be explicitly assigned if necessary.
        /// </summary>
        public string FaultCode { get; set; }


        /// <summary>
        /// Gets the type of the fault details.
        /// </summary>
        public Type FaultType { get; }
    }
}
