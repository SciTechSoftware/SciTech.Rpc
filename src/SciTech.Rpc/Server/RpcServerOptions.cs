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

namespace SciTech.Rpc.Server
{

    /// <summary>
    /// Defines options for the server side implementation of RPC services.
    /// </summary>
    /// <remarks>
    /// Service options can be specified at many different levels. Below is a list of where options can be specified, with
    /// the highest priority level first. Higher priority levels will override properties at lower levels. Collections will
    /// be combined for all levels, but if the same item is included in several places the higher priority item will take precedence.
    /// For example, if more than one exception converter with the same <see cref="IRpcServerExceptionConverter.FaultCode"/> fault code exists, 
    /// the higher priority converter will be used.
    /// <list type="number">
    /// <item><description>
    ///     Attributes on service operation<br/>
    ///     This includes attributes such as <see cref = "RpcFaultConverterAttribute" />, <see cref = "RpcFaultAttribute" />,
    ///     and <see cref="RpcOperationAttribute"/> settings.
    /// </description></item>
    /// <item><description>
    ///     Attributes on service interface<br/>
    ///     This includes attributes such as <see cref="RpcFaultConverterAttribute"/>, <see cref="RpcFaultAttribute"/>,
    ///     and <see cref="RpcServiceAttribute"/> settings.
    /// </description></item>
    /// <item><description>
    ///     Registered service options<br/>
    ///     This includes <see cref="RpcServiceOptions{T}"/> provided when registering service types, for instance using <see cref="IRpcServiceRegistration"/>, 
    ///     or <see cref="IRpcServiceDefinitionsBuilder.RegisterService(Type, Type?, IRpcServerOptions?)"/>.
    /// </description></item>
    /// <item><description>
    ///     Provided server options<br/>
    ///     This includes <see cref="RpcServerOptions"/> provided when creating the <see cref="IRpcServer"/> implementation.
    /// </description></item>
    /// <item><description>
    ///     <see cref="IRpcServiceDefinitionsProvider"/> options<br/>
    ///     This includes <see cref="RpcServerOptions"/> provided when creating the optional <c>IRpcServiceDefinitionsProvider</c> implementation,
    ///     e.g. <see cref="RpcServiceDefinitionsBuilder"/>.
    /// </description></item>
    /// </list>
    /// </remarks>
    public interface IRpcServerOptions
    {
        /// <summary>
        /// Gets a value indicating whether service instances may be automatically published
        /// when returned from a service implementation method.
        /// </summary>
        bool? AllowAutoPublish { get; }

        /// <summary>
        /// Gets a value indicating whether the singleton services can be discovered.
        /// </summary>
        public bool? AllowDiscovery { get; }


        IReadOnlyList<IRpcServerExceptionConverter> ExceptionConverters { get; }

        IReadOnlyList<RpcServerCallInterceptor> Interceptors { get; }

        bool IsEmpty { get; }

        /// <summary>
        /// Gets or sets the maximum message size in bytes that can be received by the server.
        /// </summary>
        int? ReceiveMaxMessageSize { get; }

        /// <summary>
        /// Gets or sets the maximum message size in bytes that can be sent from the server.
        /// </summary>
        int? SendMaxMessageSize { get; }

        IRpcSerializer? Serializer { get; }

        ImmutableRpcServerOptions AsImmutable();
    }

    /// <summary>
    /// Mutable implementation of <see cref="IRpcServerOptions"/>, for providing options for 
    /// the server side implementation of RPC services.
    /// </summary>
    public class RpcServerOptions : IRpcServerOptions
    {
        private List<IRpcServerExceptionConverter>? exceptionConverters;

        private List<RpcServerCallInterceptor>? interceptors;

        /// <summary>
        /// Gets or sets a value indicating whether service instances may be automatically published
        /// when returned from a service implementation method.
        /// </summary>
        public bool? AllowAutoPublish { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the singleton services can be discovered.
        /// </summary>
        public bool? AllowDiscovery { get; set; }

        public List<IRpcServerExceptionConverter> ExceptionConverters
        {
            get
            {
                if (this.exceptionConverters == null)
                {
                    this.exceptionConverters = new List<IRpcServerExceptionConverter>();
                }

                return this.exceptionConverters;
            }
        }

        public List<RpcServerCallInterceptor> Interceptors
        {
            get
            {
                if (this.interceptors == null)
                {
                    this.interceptors = new List<RpcServerCallInterceptor>();
                }

                return this.interceptors;
            }
        }

        public bool IsEmpty
        {
            get => (this.exceptionConverters == null || this.exceptionConverters.Count == 0)
                && (this.interceptors == null || this.interceptors.Count == 0)
                && this.AllowAutoPublish == null
                && this.Serializer == null
                && this.ReceiveMaxMessageSize == null
                && this.SendMaxMessageSize == null
                && this.Serializer != null;
        }

        /// <summary>
        /// Gets or sets the maximum message size in bytes that can be received by the server.
        /// </summary>
        public int? ReceiveMaxMessageSize { get; set; }

        /// <summary>
        /// Gets or sets the maximum message size in bytes that can be sent from the server.
        /// </summary>
        public int? SendMaxMessageSize { get; set; }

        public IRpcSerializer? Serializer { get; set; }

        IReadOnlyList<IRpcServerExceptionConverter> IRpcServerOptions.ExceptionConverters => this.ExceptionConverters;

        IReadOnlyList<RpcServerCallInterceptor> IRpcServerOptions.Interceptors => this.Interceptors;

        public ImmutableRpcServerOptions AsImmutable()
        {
            return !this.IsEmpty ? new ImmutableRpcServerOptions(this) : ImmutableRpcServerOptions.Empty;
        }
    }

    /// <summary>
    /// Specialization of <see cref="RpcServerOptions"/> than can be used to configure
    /// service specific server options.
    /// </summary>
    /// <typeparam name="T">Type of the service interface.</typeparam>
    public class RpcServiceOptions<T> : RpcServerOptions
    {
    }
}
