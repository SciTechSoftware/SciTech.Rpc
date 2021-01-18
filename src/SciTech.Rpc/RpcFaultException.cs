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

using SciTech.Rpc.Serialization;
using System;
using System.ComponentModel;
using System.Reflection;

namespace SciTech.Rpc
{
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false)]
    public sealed class RpcFaultDetailsAttribute : Attribute
    {
        public RpcFaultDetailsAttribute(string faultCode)
        {
            if (string.IsNullOrWhiteSpace(faultCode))
            {
                throw new ArgumentException("Fault code may not be empty.", nameof(faultCode));
            }

            this.FaultCode = faultCode;
        }

        public string FaultCode { get; }
    }

    /// <summary>
    /// Thrown when an undeclared exception occurs within an  operation handler.
    /// </summary>
    public class RpcFaultException : RpcBaseException
    {
        public RpcFaultException(string faultCode, string message) : this(faultCode, message, null)
        {
        }

        public RpcFaultException(string faultCode, string message, Exception? innerException)
            : base(message, innerException)
        {
            this.FaultCode = faultCode ?? throw new ArgumentNullException(nameof(faultCode));

        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public virtual byte[]? SerializeDetails(IRpcSerializer serializer) => null;

        public virtual Type? DetailsType => null;

        public string FaultCode { get; }
    }

    public class RpcFaultException<TFault> : RpcFaultException where TFault : class
    {
        public RpcFaultException(string? faultCode, string? message, TFault fault)
            : base(faultCode ?? RetrieveFaultCode(), !string.IsNullOrWhiteSpace(message) ? message! : "RPC fault without a message.")
        {
            this.Fault = fault;
        }
        public RpcFaultException(TFault fault) : this(null, null, fault)
        {
        }

        public RpcFaultException(string? message, TFault fault)
            : this(null, !string.IsNullOrWhiteSpace(message) ? message! : "RPC fault without a message.", fault)
        {
        }

        public override Type? DetailsType => typeof(TFault);


        [EditorBrowsable(EditorBrowsableState.Never)]
        public override byte[]? SerializeDetails(IRpcSerializer serializer) => this.Fault != default ? serializer.Serialize(this.Fault) :  null;


        public TFault Fault { get; }


        internal static string RetrieveFaultCode()
        {
            string retrievedFaultCode;
            var detailsAttribute = typeof(TFault).GetCustomAttribute<RpcFaultDetailsAttribute>();
            if (detailsAttribute != null)
            {
                retrievedFaultCode = detailsAttribute.FaultCode;
            }
            else
            {
                retrievedFaultCode = typeof(TFault).Name;
            }

            return retrievedFaultCode;
        }
    }
}
