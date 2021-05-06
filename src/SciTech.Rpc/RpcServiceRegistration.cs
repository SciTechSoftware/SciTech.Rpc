#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using SciTech.Rpc.Server;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace SciTech.Rpc
{
    /// <summary>
    /// Provides registration data for one or more RPC service types. Can be used to provide initial registrations when creating an <see cref="IRpcServer"/>.
    /// </summary>
    public interface IRpcServiceRegistration
    {
        /// <summary>
        /// Gets the options for the services provided buy this <see cref="IRpcServiceRegistration"/>. The options may be overridden by options provided by 
        /// <see cref="RegisteredServiceType.ServerOptions">RegisteredServiceType.ServerOptions</see>.
        /// </summary>
        public IRpcServerOptions? ServerOptions { get; }

        /// <summary>
        /// Gets an enumerable of interface types that implement RPC services.
        /// </summary>
        IEnumerable<RegisteredServiceType> GetServiceTypes(RpcServiceDefinitionSide definitionType);
    }

    /// <summary>
    /// Contains information about a registered service type.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815: Override equals and operator equals on value types")]
    public struct RegisteredServiceType
    {
        internal RegisteredServiceType(Type serviceType, Type? implementationType, IRpcServerOptions? serverOptions)
        {
            this.ServiceType = serviceType;
            this.ServerOptions = serverOptions;
            this.ImplementationType = implementationType;
        }

        /// <summary>
        /// Gets the server options for the registered service type.
        /// </summary>
        public IRpcServerOptions? ServerOptions { get; }

        /// <summary>
        /// Gets the type of the service interface. The interface type should be marked with <see cref="RpcServiceAttribute"/>.
        /// </summary>
        public Type ServiceType { get; }


        /// <summary>
        /// Gets the optional implementation type of the interface service.
        /// </summary>
        public Type? ImplementationType { get; }

        public override string ToString()
        {
            return this.ImplementationType != null ? $"{this.ServiceType} ({this.ImplementationType})" : $"{this.ServiceType}";
        }
    }

    public class KnownSerializationType
    {
        public KnownSerializationType(Type knownType)
        {
            this.KnownType = knownType ?? throw new ArgumentNullException(nameof(knownType));
        }

        public KnownSerializationType(Type knownType, int order)
        {
            this.KnownType = knownType ?? throw new ArgumentNullException(nameof(knownType));
            this.Order = order;
        }

        public Type KnownType { get; }

        public int Order { get; }
    }

    /// <summary>
    /// Implementation of <see cref="IRpcServiceRegistration"/> that provides registration for a single service type.
    /// </summary>
    public class RpcServiceRegistration : IRpcServiceRegistration
    {
        public RpcServiceRegistration(Type serviceType)
        {
            this.ServiceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
        }

        public RpcServiceRegistration(Type serviceType, IRpcServerOptions? serverOptions)
        {
            this.ServiceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
            this.ServerOptions = serverOptions;
        }

        public IRpcServerOptions? ServerOptions { get; }

        public Type ServiceType { get; }

        public IEnumerable<RegisteredServiceType> GetServiceTypes(RpcServiceDefinitionSide definitionType)
        {
            var rpcServiceAttribute = this.ServiceType.GetCustomAttribute<RpcServiceAttribute>(false);
            if (rpcServiceAttribute != null
                && (definitionType == RpcServiceDefinitionSide.Both
                || rpcServiceAttribute.ServiceDefinitionSide == RpcServiceDefinitionSide.Both
                || definitionType == rpcServiceAttribute.ServiceDefinitionSide))
            {
                yield return new RegisteredServiceType(this.ServiceType, null, this.ServerOptions);
            }
        }
    }

    /// <summary>
    /// Implementation of <see cref="IRpcServiceRegistration"/> that provides registration for all service type in an assembly.
    /// </summary>
    public class RpcServicesAssemblyRegistration : IRpcServiceRegistration
    {
        /// <summary>
        /// Initializes a new service registration for all service types in the provided <paramref name="servicesAssembly"/>.
        /// </summary>
        /// <param name="servicesAssembly">Assembly containing the service types to register.</param>
        /// <param name="serverOptions">Options server options for the registered services.</param>
        public RpcServicesAssemblyRegistration(Assembly servicesAssembly, IRpcServerOptions? serverOptions = null)
        {
            this.ServicesAssembly = servicesAssembly;
            this.ServerOptions = serverOptions;
        }

        public IRpcServerOptions? ServerOptions { get; }

        public Assembly ServicesAssembly { get; }

        public IEnumerable<RegisteredServiceType> GetServiceTypes(RpcServiceDefinitionSide definitionType)
        {
            foreach (var type in this.ServicesAssembly.ExportedTypes)
            {
                if (type.IsInterface)
                {
                    var rpcServiceAttribute = type.GetCustomAttribute<RpcServiceAttribute>(false);
                    if (rpcServiceAttribute != null
                        && (definitionType == RpcServiceDefinitionSide.Both
                        || rpcServiceAttribute.ServiceDefinitionSide == RpcServiceDefinitionSide.Both
                        || definitionType == rpcServiceAttribute.ServiceDefinitionSide))
                    {
                        yield return new RegisteredServiceType(type, null, this.ServerOptions);
                    }
                }
            }
        }
    }
}
