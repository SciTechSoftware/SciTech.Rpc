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
using System.Reflection;

namespace SciTech.Rpc
{
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false)]
    public class RpcFaultDetailsAttribute : Attribute
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

    public class RpcFaultException : Exception
    {
        private string? assignedFaultCode;

        public RpcFaultException() : this("", "", null)
        {
        }

        public RpcFaultException(string faultCode, string message) : this(faultCode, message, null)
        {
        }

        public RpcFaultException(string faultCode, string message, Exception? innerException)
            : base(message, innerException)
        {
            this.assignedFaultCode = faultCode ?? throw new ArgumentNullException(nameof(faultCode));

        }

        protected RpcFaultException(string message) : this(message, (Exception?)null)
        {
        }

        protected RpcFaultException(string message, Exception? innerException)
            : base(message, innerException)
        {
        }

        public virtual string FaultCode
        {
            get
            {
                if (this.assignedFaultCode == null)
                {
#pragma warning disable CA1065 // Do not raise exceptions in unexpected locations
                    throw new NotImplementedException("FaultCode must be implemented by derived class.");
#pragma warning restore CA1065 // Do not raise exceptions in unexpected locations
                }

                return this.assignedFaultCode;
            }
        }
    }

#pragma warning disable CA1032 // Implement standard exception constructors
    public class RpcFaultException<TFault> : RpcFaultException
    {
        private string? retrievedFaultCode;

        public RpcFaultException(TFault fault) : this(null, fault)
        {
        }

        public RpcFaultException(string? message, TFault fault)
            : base(!string.IsNullOrWhiteSpace(message) ? message : "RPC fault without a message.")
        {
            this.Fault = fault;
        }

        public TFault Fault { get; }

        public override string FaultCode
        {
            get
            {
                if (this.retrievedFaultCode == null)
                {
                    var detailsAttribute = typeof(TFault).GetCustomAttribute<RpcFaultDetailsAttribute>();
                    if (detailsAttribute != null)
                    {
                        this.retrievedFaultCode = detailsAttribute.FaultCode;
                    }
                    else
                    {
                        this.retrievedFaultCode = typeof(TFault).Name;
                    }
                }

                return this.retrievedFaultCode;
            }
        }
    }
#pragma warning restore CA1032 // Implement standard exception constructors
}
