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

using System;

namespace SciTech.Rpc.Internal
{
    internal sealed class RpcFaultExceptionConverter<TFault> : RpcExceptionConverter<RpcFaultException<TFault>, TFault>
        where TFault : class
    {
        public static readonly RpcFaultExceptionConverter<TFault> Default = new RpcFaultExceptionConverter<TFault>();

        public RpcFaultExceptionConverter(string faultCode) : base(faultCode, false)
        {
        }

        public RpcFaultExceptionConverter() : base(false)
        {
        }

        public override RpcFaultException<TFault> CreateException(string message, TFault details)
        {
            return new RpcFaultException<TFault>(message, details);
        }

        public override RpcFaultException CreateFault(RpcFaultException<TFault> exception)
        {
            if (exception is null) throw new ArgumentNullException(nameof(exception));

            return new RpcFaultException<TFault>(exception.Message, exception.Fault);
        }
    }
}