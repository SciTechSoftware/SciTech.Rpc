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
#pragma warning disable CA1051 // Do not declare visible instance fields
        [DataMember(Order = 1)]
        public string? ErrorType;

        [DataMember(Order = 2)]
        public string? FaultCode;

        [DataMember(Order = 3)]
        public byte[]? FaultDetails;

        [DataMember(Order = 4)]
        public string? Message;

        public RpcError() { }
#pragma warning restore CA1051 // Do not declare visible instance fields
    }

    [DataContract]
    public sealed class RpcResponse
    {
        public RpcResponse() { }
    }

    [DataContract]
    public sealed class RpcResponse<T>
    {
#pragma warning disable CA1051 // Do not declare visible instance fields
#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        /// <summary>
        /// Result should be marked as nullable (?) since
        /// it may return null reference types. 
        /// </summary>
        [DataMember(Order = 1)]
        public T Result;

        public RpcResponse()
        {
            //this.Result = default!;
        }

        public RpcResponse(T result)
        {
            this.Result = result;
        }
#pragma warning restore CA1051 // Do not declare visible instance fields
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
    }



    [DataContract]
    public sealed class RpcResponseWithError
    {
#pragma warning disable CA1051 // Do not declare visible instance fields
        [DataMember(Order = 2)]
        public RpcError? Error;

        public RpcResponseWithError() { }

        public RpcResponseWithError(RpcError error)
        {
            this.Error = error;
        }
#pragma warning restore CA1051 // Do not declare visible instance fields
    }

    [DataContract]
    public sealed class RpcResponseWithError<T>
    {
#pragma warning disable CA1051 // Do not declare visible instance fields
#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        [DataMember(Order = 2)]
        public RpcError? Error;

        /// <summary>
        /// Result should be marked as nullable (?) since
        /// it may return null reference types. 
        /// </summary>
        [DataMember(Order = 1)]
        public T Result;

        public RpcResponseWithError()
        {
            //this.Result = default!;
        }

        public RpcResponseWithError(T result)
        {
            this.Result = result;
        }

        public RpcResponseWithError(RpcError error)
        {
            this.Error = error;
        }
#pragma warning restore CA1051 // Do not declare visible instance fields
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
    }
}
