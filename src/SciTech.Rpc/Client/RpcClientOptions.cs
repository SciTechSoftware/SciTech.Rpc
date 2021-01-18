#region Copyright notice and license

// Copyright (c) 2019-2021, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License.
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//

#endregion Copyright notice and license

using Microsoft.Extensions.Options;
using SciTech.Rpc.Serialization;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace SciTech.Rpc.Client
{
    /// <summary>
    /// Defines RPC client options. For more information, see the implementations <see cref="RpcClientOptions"/> and
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
        /// Gets a list of known service types.
        /// </summary>
        IReadOnlyList<Type> KnownServiceTypes { get; }

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
    /// Mutable implementation of <see cref="IRpcClientOptions"/> that can be used to build options before
    /// creating an RPC connection.
    /// </summary>
    public class RpcClientOptions : IRpcClientOptions
    {
        private List<IRpcClientExceptionConverter>? exceptionConverters;

        private List<RpcClientCallInterceptor>? interceptors;

        private List<Type>? knownServiceTypes;

        public RpcClientOptions(IOptions<RpcClientOptions> options)
        {
            if (options is null) throw new ArgumentNullException(nameof(options));

            var o = options.Value;

            this.CallTimeout = o.CallTimeout;
            this.StreamingCallTimeout = o.StreamingCallTimeout;

            if (o.exceptionConverters?.Count > 0)
            {
                this.ExceptionConverters.AddRange(o.exceptionConverters);
            }
            if (o.interceptors?.Count > 0)
            {
                this.Interceptors.AddRange(o.interceptors);
            }
            if (o.knownServiceTypes?.Count > 0)
            {
                this.KnownServiceTypes.AddRange(o.knownServiceTypes);
            }

            this.ReceiveMaxMessageSize = o.ReceiveMaxMessageSize;
            this.SendMaxMessageSize = o.SendMaxMessageSize;
            this.Serializer = o.Serializer;
        }

        public RpcClientOptions()
        {

        }

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
                    && (this.knownServiceTypes == null || this.knownServiceTypes.Count == 0 )
                    && this.ReceiveMaxMessageSize == null
                    && this.SendMaxMessageSize == null
                    && this.CallTimeout == null
                    && this.StreamingCallTimeout == null
                    && this.Serializer == null;
            }
        }

        public List<Type> KnownServiceTypes
        {
            get
            {
                if (this.knownServiceTypes == null)
                {
                    this.knownServiceTypes = new List<Type>();
                }

                return this.knownServiceTypes;
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
        IReadOnlyList<Type> IRpcClientOptions.KnownServiceTypes => this.KnownServiceTypes;

        public void AddAssemblyServiceTypes(Assembly servicesAssembly)
        {
            if (servicesAssembly is null) throw new ArgumentNullException(nameof(servicesAssembly));

            foreach (var type in servicesAssembly.ExportedTypes)
            {
                if (type.IsInterface)
                {
                    var rpcServiceAttribute = type.GetCustomAttribute<RpcServiceAttribute>(false);
                    if (rpcServiceAttribute != null
                        && (rpcServiceAttribute.ServiceDefinitionSide == RpcServiceDefinitionSide.Both
                        || rpcServiceAttribute.ServiceDefinitionSide == RpcServiceDefinitionSide.Client))
                    {
                        this.KnownServiceTypes.Add(type);
                    }
                }
            }
        }

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