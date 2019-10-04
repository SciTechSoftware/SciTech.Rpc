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
using System.Linq;
using System.Runtime.Serialization;

namespace SciTech.Rpc.Internal
{
    [DataContract]
    public class RpcError
    {
        [DataMember(Order = 1)]
        public string? ErrorType { get; set; }

        [DataMember(Order = 2)]
        public string? FaultCode { get; set; }

        [DataMember(Order = 3)]
#pragma warning disable CA1819 // Properties should not return arrays
        public byte[]? FaultDetails { get; set; }
#pragma warning restore CA1819 // Properties should not return arrays

        [DataMember(Order = 4)]
        public string? Message { get; set; }

        public RpcError() { }
    }

    [DataContract]
    public sealed class RpcResponse
    {
        public RpcResponse() { }
    }

    [DataContract]
    public sealed class RpcResponse<T>
    {
        /// <summary>
        /// Result should be marked as nullable (?) since
        /// it may return null reference types. 
        /// </summary>
        [DataMember(Order = 1)]
        public T Result { get; set; }

#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        public RpcResponse()
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        {
        }

        public RpcResponse(T result)
        {
            this.Result = result;
        }
    }



    [DataContract]
    public sealed class RpcResponseWithError
    {
        [DataMember(Order = 2)]
        public RpcError? Error { get; set; }

        public RpcResponseWithError() { }

        public RpcResponseWithError(RpcError error)
        {
            this.Error = error;
        }
    }

    [DataContract]
    public sealed class RpcResponseWithError<T>
    {
        [DataMember(Order = 2)]
        public RpcError? Error { get; set; }

        /// <summary>
        /// Result should be marked as nullable (?) since
        /// it may return null reference types. 
        /// </summary>
        [DataMember(Order = 1)]
        public T Result { get; set; }

#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        public RpcResponseWithError()
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        {
        }

        public RpcResponseWithError(T result)
        {
            this.Result = result;
        }

#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        public RpcResponseWithError(RpcError error)
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        {
            this.Error = error;
        }
    }
}
