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

using SciTech.Rpc.Serialization;
using SciTech.Collections.Immutable;
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

        public bool? SilentStreamingCancellation { get; private set; }

        /// <inheritdoc cref="IRpcServerOptions.ExceptionConverters"/>
        /// <remarks>The list is immutable.</remarks>
        public IReadOnlyList<IRpcServerExceptionConverter> ExceptionConverters { get; private set; } = ImmutableArrayList<IRpcServerExceptionConverter>.Empty;

        /// <inheritdoc cref="IRpcServerOptions.Interceptors"/>
        /// <remarks>The list is immutable.</remarks>
        public IReadOnlyList<RpcServerCallInterceptor> Interceptors { get; private set; } = ImmutableArrayList<RpcServerCallInterceptor>.Empty;

        /// <inheritdoc cref="IRpcServerOptions.ServerProperties"/>
        public ImmutableDictionary<string, string> ServerProperties { get; private set; } = ImmutableDictionary<string, string>.Empty;

        public bool IsEmpty
        {
            get => this.ExceptionConverters.Count == 0
                && this.Interceptors.Count == 0
                && this.ServerProperties.Count == 0
                && this.AllowAutoPublish == null
                && this.AllowDiscovery == null
                && this.Serializer == null
                && this.ReceiveMaxMessageSize == null
                && this.SendMaxMessageSize == null
                && this.Serializer == null
                && this.SilentStreamingCancellation == null;
        }


        public int? ReceiveMaxMessageSize { get; private set; }

        public int? SendMaxMessageSize { get; private set; }

        public IRpcSerializer? Serializer { get; private set; }

        public TimeSpan? StreamingCallTimeout { get; private set; }

        IReadOnlyDictionary<string, string> IRpcServerOptions.ServerProperties => this.ServerProperties;

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

        public ImmutableRpcServerOptions AsImmutable() => this;

        private void Assign(IRpcServerOptions? options)
        {
            if (options != null)
            {
                this.AllowAutoPublish = options.AllowAutoPublish ?? this.AllowAutoPublish;
                this.AllowDiscovery = options.AllowDiscovery ?? this.AllowDiscovery;
                this.ExceptionConverters = options.ExceptionConverters.Concat(this.ExceptionConverters).ToImmutableArrayList();
                this.Interceptors = options.Interceptors.Concat(this.Interceptors).ToImmutableArrayList();
                this.ServerProperties = this.ServerProperties.SetItems(options.ServerProperties);
                this.ReceiveMaxMessageSize = options.ReceiveMaxMessageSize ?? this.ReceiveMaxMessageSize;
                this.SendMaxMessageSize = options.SendMaxMessageSize ?? this.SendMaxMessageSize;
                this.Serializer = options.Serializer ?? this.Serializer;
                this.SilentStreamingCancellation = options.SilentStreamingCancellation ?? this.SilentStreamingCancellation;
            }
        }
    }
}
