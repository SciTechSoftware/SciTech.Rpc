﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace SciTech.Rpc.Client
{
    /// <summary>
    /// Immutable variant of <see cref="RpcClientOptions"/>. Once client options have been 
    /// assigend to a connection or associated with a service they should no longer be modified and
    /// will only be accessible through this class.
    /// </summary>
    public class ImmutableRpcClientOptions : IRpcClientOptions
    {
        public static readonly ImmutableRpcClientOptions Empty = new ImmutableRpcClientOptions(null);

        public ImmutableRpcClientOptions(RpcClientOptions? options)
        {
            this.Assign(options);
        }

        public TimeSpan? CallTimeout { get; private set; }

        public ImmutableArray<IRpcClientExceptionConverter> ExceptionConverters { get; private set; }

        public ImmutableArray<RpcClientCallInterceptor> Interceptors { get; private set; }

        public bool IsEmpty
        {
            get
            {
                return (this.ExceptionConverters.Length == 0)
                    && (this.Interceptors.Length == 0)
                    && this.ReceiveMaxMessageSize == null
                    && this.SendMaxMessageSize == null
                    && this.CallTimeout == null
                    && this.StreamingCallTimeout == null;
            }
        }

        /// <summary>
        /// Gets the maximum message size in bytes that can be received by the client.
        /// </summary>
        public int? ReceiveMaxMessageSize { get; private set; }

        /// <summary>
        /// Gets the maximum message size in bytes that can be sent from the client.
        /// </summary>
        public int? SendMaxMessageSize { get; private set; }

        public IRpcSerializer? Serializer { get; private set; }

        public TimeSpan? StreamingCallTimeout { get; private set; }

        IReadOnlyList<IRpcClientExceptionConverter> IRpcClientOptions.ExceptionConverters => this.ExceptionConverters;

        IReadOnlyList<RpcClientCallInterceptor> IRpcClientOptions.Interceptors => this.Interceptors;

        internal static ImmutableRpcClientOptions Combine(params IRpcClientOptions?[] options)
        {
            if (options != null)
            {
                if (options.Any(o => o != null && !o.IsEmpty))
                {
                    var combinedOptions = new ImmutableRpcClientOptions(null);

                    foreach (var o in options)
                    {
                        combinedOptions.Assign(o);
                    }

                    return combinedOptions;
                }
            }

            return Empty;
        }

        private void Assign(IRpcClientOptions? options)
        {
            if (options != null)
            {
                this.CallTimeout = options.CallTimeout ?? this.CallTimeout;
                this.StreamingCallTimeout = options.StreamingCallTimeout ?? this.StreamingCallTimeout;
                this.ExceptionConverters = options.ExceptionConverters?.ToImmutableArray() ?? this.ExceptionConverters;
                this.Interceptors = options.Interceptors?.ToImmutableArray() ?? this.Interceptors;
                this.ReceiveMaxMessageSize = options.ReceiveMaxMessageSize ?? this.ReceiveMaxMessageSize;
                this.SendMaxMessageSize = options.SendMaxMessageSize ?? this.SendMaxMessageSize;
                this.Serializer = options.Serializer ?? this.Serializer;
            }
        }
    }
}
