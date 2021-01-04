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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.Serialization;

namespace SciTech.Rpc.Internal
{

    [DataContract]
    public sealed class RpcResponse
    {
        public RpcResponse() { }
    }

    [DataContract]
    public sealed class RpcResponse<T>
    {
        [DataMember(Order = 1)]
        [AllowNull]
        public T Result { get; set; }

        public RpcResponse()
        {
        }

        public RpcResponse(T result)
        {
            this.Result = result;
        }
    }
}
