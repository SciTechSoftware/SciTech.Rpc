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
using System.Collections.Generic;

namespace SciTech.Rpc.Client
{
    public interface IRpcClientOptions
    {
        TimeSpan? CallTimeout { get; }

        IReadOnlyList<IRpcClientExceptionConverter> ExceptionConverters { get; }

        IReadOnlyList<RpcClientCallInterceptor> Interceptors { get; }

        bool IsEmpty { get; }

        /// <summary>
        /// Gets the maximum message size in bytes that can be received by the client.
        /// </summary>
        int? ReceiveMaxMessageSize { get; }

        /// <summary>
        /// Gets the maximum message size in bytes that can be sent from the client.
        /// </summary>
        int? SendMaxMessageSize { get; }

        IRpcSerializer? Serializer { get; }

        TimeSpan? StreamingCallTimeout { get; }

        ImmutableRpcClientOptions AsImmutable();
    }

    /// <summary>
    /// Contains options for the client side connection to RPC services.
    /// </summary>
    public class RpcClientOptions : IRpcClientOptions
    {
        private List<IRpcClientExceptionConverter>? exceptionConverters;

        private List<RpcClientCallInterceptor>? interceptors;

        public TimeSpan? CallTimeout { get; set; }

        public List<IRpcClientExceptionConverter> ExceptionConverters
        {
            get
            {
                if (this.exceptionConverters == null)
                {
                    this.exceptionConverters = new List<IRpcClientExceptionConverter>();
                }

                return this.exceptionConverters;
            }
        }

        public List<RpcClientCallInterceptor> Interceptors
        {
            get
            {
                if (this.interceptors == null)
                {
                    this.interceptors = new List<RpcClientCallInterceptor>();
                }

                return this.interceptors;
            }
        }

        public bool IsEmpty
        {
            get
            {
                return (this.exceptionConverters == null || this.exceptionConverters.Count == 0)
                    && (this.interceptors == null || this.interceptors.Count == 0)
                    && this.ReceiveMaxMessageSize == null
                    && this.SendMaxMessageSize == null
                    && this.CallTimeout == null
                    && this.StreamingCallTimeout == null
                    && this.Serializer == null;

            }
        }

        /// <summary>
        /// Gets or sets the maximum message size in bytes that can be received by the client.
        /// </summary>
        public int? ReceiveMaxMessageSize { get; set; }

        /// <summary>
        /// Gets or sets the maximum message size in bytes that can be sent from the client.
        /// </summary>
        public int? SendMaxMessageSize { get; set; }

        public IRpcSerializer? Serializer { get; set; }

        public TimeSpan? StreamingCallTimeout { get; set; }

        IReadOnlyList<IRpcClientExceptionConverter> IRpcClientOptions.ExceptionConverters => this.ExceptionConverters;

        IReadOnlyList<RpcClientCallInterceptor> IRpcClientOptions.Interceptors => this.Interceptors;

        public ImmutableRpcClientOptions AsImmutable()
        {
            return new ImmutableRpcClientOptions(this);
        }
    }

    /// <summary>
    /// Specialization of <see cref="RpcClientOptions"/> than can be used to configure
    /// service specific client options.
    /// </summary>
    /// <typeparam name="T">Type of the service interface.</typeparam>
    public class RpcClientServiceOptions<T> : RpcClientOptions
    {
    }
}
