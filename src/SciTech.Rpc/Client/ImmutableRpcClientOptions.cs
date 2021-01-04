#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using SciTech.Rpc.Internal;
using SciTech.Rpc.Serialization;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;

namespace SciTech.Rpc.Client
{
    /// <summary>
    /// Immutable variant of <see cref="RpcClientOptions"/>. Once client options have been 
    /// assigned to a channel or associated with a service they should no longer be modified and
    /// will only be accessible through this class.
    /// </summary>
    public sealed class ImmutableRpcClientOptions : IRpcClientOptions
    {
        public static readonly ImmutableRpcClientOptions Empty = new ImmutableRpcClientOptions(null);

        public ImmutableRpcClientOptions(IRpcClientOptions? options)
        {
            this.Assign(options);

            this.BuildKnownServiceTypesDictionary();
        }

        /// <inheritdoc/>
        public TimeSpan? CallTimeout { get; private set; }

        /// <inheritdoc cref="IRpcClientOptions.ExceptionConverters"/>
        public ImmutableArray<IRpcClientExceptionConverter> ExceptionConverters { get; private set; }
            = ImmutableArray<IRpcClientExceptionConverter>.Empty;

        /// <inheritdoc cref="IRpcClientOptions.Interceptors"/>
        public ImmutableArray<RpcClientCallInterceptor> Interceptors { get; private set; }
            = ImmutableArray<RpcClientCallInterceptor>.Empty;

        public ImmutableArray<Type> KnownServiceTypes { get; private set; }
            = ImmutableArray<Type>.Empty;

        /// <inheritdoc/>
        public bool IsEmpty
        {
            get
            {
                return this.ExceptionConverters.Length == 0
                    && this.Interceptors.Length == 0
                    && this.KnownServiceTypes.Length == 0
                    && this.ReceiveMaxMessageSize == null
                    && this.SendMaxMessageSize == null
                    && this.CallTimeout == null
                    && this.StreamingCallTimeout == null
                    && this.Serializer == null;
            }
        }

        /// <inheritdoc/>
        public int? ReceiveMaxMessageSize { get; private set; }

        /// <inheritdoc/>
        public int? SendMaxMessageSize { get; private set; }

        /// <inheritdoc/>
        public IRpcSerializer? Serializer { get; private set; }

        /// <inheritdoc/>
        public TimeSpan? StreamingCallTimeout { get; private set; }

        /// <inheritdoc/>
        IReadOnlyList<IRpcClientExceptionConverter> IRpcClientOptions.ExceptionConverters => this.ExceptionConverters;

        /// <inheritdoc/>
        IReadOnlyList<RpcClientCallInterceptor> IRpcClientOptions.Interceptors => this.Interceptors;

        IReadOnlyList<Type> IRpcClientOptions.KnownServiceTypes => this.KnownServiceTypes;

        /// <summary>
        /// Gets the <see cref="KnownServiceTypes"/> as a dictionary, with the service name as the key.
        /// </summary>
        public IReadOnlyDictionary<string, ImmutableArray<Type>> KnownServiceTypesDictionary { get; private set; } 
            = ImmutableDictionary<string, ImmutableArray<Type>>.Empty;

        /// <inheritdoc/>
        ImmutableRpcClientOptions IRpcClientOptions.AsImmutable() => this;

        public static ImmutableRpcClientOptions Combine(params IRpcClientOptions?[] options)
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

                    combinedOptions.BuildKnownServiceTypesDictionary();
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
                this.ExceptionConverters = this.ExceptionConverters.AddRange(options.ExceptionConverters);
                this.Interceptors = this.Interceptors.AddRange(options.Interceptors);
                this.KnownServiceTypes = this.KnownServiceTypes.AddRange(options.KnownServiceTypes);
                this.ReceiveMaxMessageSize = options.ReceiveMaxMessageSize ?? this.ReceiveMaxMessageSize;
                this.SendMaxMessageSize = options.SendMaxMessageSize ?? this.SendMaxMessageSize;
                this.Serializer = options.Serializer ?? this.Serializer;
            }
        }

        private void BuildKnownServiceTypesDictionary()
        {
            Dictionary<string, List<Type>> knownServices = new Dictionary<string, List<Type>>();

            foreach ( var serviceType in this.KnownServiceTypes)
            {
                var interfaceServices = RpcBuilderUtil.GetAllServices(serviceType, RpcServiceDefinitionSide.Client, false);
                foreach (var serviceInfo in interfaceServices)
                {
                    if (knownServices.TryGetValue(serviceInfo.FullName, out var services))
                    {
                        if (services.Find(s => s.Equals(serviceInfo.Type)) == null)
                        {
                            services.Add(serviceInfo.Type);
                        }
                    }
                    else
                    {
                        services = new List<Type>
                        {
                            serviceInfo.Type
                        };
                        knownServices.Add(serviceInfo.FullName, services);
                    }
                }
            }

            this.KnownServiceTypesDictionary = ImmutableDictionary.CreateRange(knownServices
                .Select(p => new KeyValuePair<string, ImmutableArray<Type>>(p.Key, p.Value.ToImmutableArray())));
        }
    }
}
