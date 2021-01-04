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

using SciTech.Rpc.Serialization;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace SciTech.Rpc.Internal
{
    [DataContract]
    public class RpcError
    {
        [DataMember(Order = 1)]
        public string? ErrorType { get; set; }

        [DataMember(Order = 2)]
        public string? ErrorCode { get; set; }
        
        //[DataMember(Order = 2)]
        //public string? FaultCode { get; set; }

        [DataMember(Order = 3)]
        [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "<Pending>")]
        public byte[]? ErrorDetails { get; set; }

        [DataMember(Order = 4)]
        public string? Message { get; set; }

        public RpcError() { }

        internal static RpcError? TryCreate(Exception e, IRpcSerializer? serializer )
        {
            switch (e)
            {
                case RpcServiceUnavailableException _:
                    return new RpcError
                    {
                        ErrorType = WellKnownRpcErrors.ServiceUnavailable,
                        Message = e.Message
                    };
                case RpcFaultException faultException:
                    var rpcError = new RpcError
                    {
                        ErrorType = WellKnownRpcErrors.Fault,
                        ErrorCode = faultException.FaultCode,
                        Message = faultException.Message
                    };
                    if (serializer != null)
                    {
                        rpcError.ErrorDetails = faultException.SerializeDetails(serializer);
                    }

                    return rpcError;
                case RpcFailureException failureException:
                    return new RpcError
                    {
                        ErrorType = WellKnownRpcErrors.Failure,
                        ErrorCode = failureException.Failure.ToString(),
                        Message = failureException.Message
                    };
            }

            return null;
        }
    }
}
