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
using System.Collections.Immutable;

namespace SciTech.Rpc.Server
{
    /// <summary>
    /// 
    /// </summary>
    public class ImmutableRpcServiceOptions
    {
        public ImmutableRpcServiceOptions(RpcServiceOptions? options)
        {
            this.AllowAutoPublish = options?.AllowAutoPublish;
            this.CallTimeout = options?.CallTimeout;
            this.ExceptionConverters = options?.ExceptionConverters?.ToImmutableArray() ?? ImmutableArray<IRpcServerExceptionConverter>.Empty;
            this.Interceptors = options?.Interceptors?.ToImmutableArray() ?? ImmutableArray<RpcServerCallInterceptor>.Empty;
            this.ReceiveMaxMessageSize = options?.ReceiveMaxMessageSize;
            this.SendMaxMessageSize = options?.SendMaxMessageSize;
            this.Serializer = options?.Serializer;
            this.StreamingCallTimeout = options?.StreamingCallTimeout;
        }

        public bool? AllowAutoPublish { get; }

        public TimeSpan? CallTimeout { get; }

        public ImmutableArray<IRpcServerExceptionConverter> ExceptionConverters { get; }

        public ImmutableArray<RpcServerCallInterceptor> Interceptors { get; }

        public int? ReceiveMaxMessageSize { get; }

        public int? SendMaxMessageSize { get; }

        public IRpcSerializer? Serializer { get; }

        public TimeSpan? StreamingCallTimeout { get; }
    }
}
