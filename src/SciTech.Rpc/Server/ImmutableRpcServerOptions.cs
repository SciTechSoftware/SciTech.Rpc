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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace SciTech.Rpc.Server
{
    /// <summary>
    /// Immutable variant of <see cref="RpcServerOptions"/>. Once server options have been 
    /// assigned to an RPC server or associated with a service they should no longer be modified and
    /// will only be accessible through this class.
    /// </summary>
    public class ImmutableRpcServerOptions : IRpcServerOptions
    {
        public static readonly ImmutableRpcServerOptions Empty = new ImmutableRpcServerOptions(null);

        public ImmutableRpcServerOptions(RpcServerOptions? options)
        {
            this.Assign(options);
        }

        public bool? AllowAutoPublish { get; private set; }

        public bool? AllowDiscovery { get; private set; }

        public ImmutableArray<IRpcServerExceptionConverter> ExceptionConverters { get; private set; } = ImmutableArray<IRpcServerExceptionConverter>.Empty;

        public ImmutableArray<RpcServerCallInterceptor> Interceptors { get; private set; } = ImmutableArray<RpcServerCallInterceptor>.Empty;

        public bool IsEmpty
        {
            get => this.ExceptionConverters.IsDefaultOrEmpty
                && this.Interceptors.IsDefaultOrEmpty
                && this.AllowAutoPublish == null
                && this.AllowDiscovery == null
                && this.Serializer == null
                && this.ReceiveMaxMessageSize == null
                && this.SendMaxMessageSize == null
                && this.Serializer != null;
        }


        public int? ReceiveMaxMessageSize { get; private set; }

        public int? SendMaxMessageSize { get; private set; }

        public IRpcSerializer? Serializer { get; private set; }

        public TimeSpan? StreamingCallTimeout { get; private set; }

        IReadOnlyList<IRpcServerExceptionConverter> IRpcServerOptions.ExceptionConverters => this.ExceptionConverters;

        IReadOnlyList<RpcServerCallInterceptor> IRpcServerOptions.Interceptors => this.Interceptors;

        public static ImmutableRpcServerOptions Combine(params IRpcServerOptions?[] options)
        {
            if (options != null)
            {
                if (options.Any(o => o != null && !o.IsEmpty))
                {
                    var combinedOptions = new ImmutableRpcServerOptions(null);

                    foreach (var o in options)
                    {
                        combinedOptions.Assign(o);
                    }

                    return combinedOptions;
                }
            }

            return Empty;
        }

        private void Assign(IRpcServerOptions? options)
        {
            if (options != null)
            {
                this.AllowAutoPublish = options.AllowAutoPublish ?? this.AllowAutoPublish;
                this.AllowDiscovery = options.AllowDiscovery ?? this.AllowDiscovery;
                this.ExceptionConverters = options.ExceptionConverters?.ToImmutableArray() ?? this.ExceptionConverters;
                this.Interceptors = options.Interceptors?.ToImmutableArray() ?? this.Interceptors;
                this.ReceiveMaxMessageSize = options.ReceiveMaxMessageSize ?? this.ReceiveMaxMessageSize;
                this.SendMaxMessageSize = options.SendMaxMessageSize ?? this.SendMaxMessageSize;
                this.Serializer = options.Serializer ?? this.Serializer;
            }
        }
    }
}
