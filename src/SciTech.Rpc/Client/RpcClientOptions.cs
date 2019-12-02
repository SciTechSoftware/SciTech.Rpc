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

namespace SciTech.Rpc.Client
{

    /// <summary>
    /// Defines RPC client options properties. For more information, see the implementations <see cref="RpcClientOptions"/> and 
    /// <see cref="ImmutableRpcClientOptions"/>.
    /// </summary>
    public interface IRpcClientOptions
    {
        /// <summary>
        /// Gets the call timeout of RPC operations. If not specifed, the 
        /// default timeout of the RPC channel implementation will be used, which is normally infinite.
        /// </summary>
        TimeSpan? CallTimeout { get; }

        /// <summary>
        /// Gets a list of exceptions converters. An exception converter can be used to convert an RPC fault to a client side exception. For
        /// more information, see <see cref="IRpcClientExceptionConverter"/>.
        /// </summary>
        IReadOnlyList<IRpcClientExceptionConverter> ExceptionConverters { get; }

        /// <summary>
        /// Gets a list of call interceptors. A call interceptor can be used to modify the call metadata
        /// before sending the request to the server.
        /// </summary>
        IReadOnlyList<RpcClientCallInterceptor> Interceptors { get; }

        /// <summary>
        /// Gets a value indicating whether the options are empty, i.e. all options properties are <c>null</c> or empty.
        /// </summary>
        bool IsEmpty { get; }

        /// <summary>
        /// Gets the maximum message size in bytes that can be received by the client. If not specifed, the 
        /// default maximum size of the RPC channel implementation will be used.
        /// </summary>
        int? ReceiveMaxMessageSize { get; }

        /// <summary>
        /// Gets the maximum message size in bytes that can be sent from the client. If not specifed, the 
        /// default maximum size of the RPC channel implementation will be used.
        /// </summary>
        int? SendMaxMessageSize { get; }

        /// <summary>
        /// Gets the serializer to use when serializing RPC requests and responses. If not specifed, the 
        /// default serializer of the RPC channel implementation will be used.
        /// </summary>
        IRpcSerializer? Serializer { get; }

        /// <summary>
        /// Gets the call timeout of for streaming RPC operations (including events). If not specifed, the 
        /// default timeout of the RPC channel implementation will be used, which is normally infinite.
        /// </summary>
        TimeSpan? StreamingCallTimeout { get; }

        /// <summary>
        /// Gets or creates an immutable version of these options.
        /// </summary>
        /// <returns></returns>
        ImmutableRpcClientOptions AsImmutable();
    }

    /// <summary>
    /// Contains options for the client side connection to RPC services.
    /// </summary>
    public class RpcClientOptions : IRpcClientOptions
    {
        private List<IRpcClientExceptionConverter>? exceptionConverters;

        private List<RpcClientCallInterceptor>? interceptors;

        /// <inheritdoc/>
        public TimeSpan? CallTimeout { get; set; }

        /// <inheritdoc cref="IRpcClientOptions.ExceptionConverters"/>
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

        /// <inheritdoc cref="IRpcClientOptions.Interceptors"/>
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

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public int? ReceiveMaxMessageSize { get; set; }

        /// <inheritdoc/>
        public int? SendMaxMessageSize { get; set; }

        /// <inheritdoc/>
        public IRpcSerializer? Serializer { get; set; }

        /// <inheritdoc/>
        public TimeSpan? StreamingCallTimeout { get; set; }

        /// <inheritdoc/>
        IReadOnlyList<IRpcClientExceptionConverter> IRpcClientOptions.ExceptionConverters => this.ExceptionConverters;

        /// <inheritdoc/>
        IReadOnlyList<RpcClientCallInterceptor> IRpcClientOptions.Interceptors => this.Interceptors;

        /// <inheritdoc/>
        public ImmutableRpcClientOptions AsImmutable()
        {
            return new ImmutableRpcClientOptions(this);
        }
    }

    // RpcClientServiceOptions<T> not implemented yet. Client options can only be specified
    // at connection level.
    ///// <summary>
    ///// Specialization of <see cref="RpcClientOptions"/> than can be used to configure
    ///// service specific client options.
    ///// </summary>
    ///// <typeparam name="T">Type of the service interface.</typeparam>
    //public class RpcClientServiceOptions<T> : RpcClientOptions
    //{
    //}
}
