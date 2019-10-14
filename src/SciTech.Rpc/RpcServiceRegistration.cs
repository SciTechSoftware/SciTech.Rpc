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

using SciTech.Rpc.Server;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace SciTech.Rpc
{
    public interface IRpcServiceRegistration
    {
        public RpcServerOptions? ServerOptions { get; }

        /// <summary>
        /// Gets a collection of interface types that implement RPC services.
        /// </summary>
        IEnumerable<RegisteredServiceType> GetServiceTypes(RpcServiceDefinitionSide definitionType);
    }

#pragma warning disable CA1815 // Override equals and operator equals on value types
    public struct RegisteredServiceType
#pragma warning restore CA1815 // Override equals and operator equals on value types
    {
        internal RegisteredServiceType(Type serviceType, Type? implementationType, RpcServerOptions? serverOptions)
        {
            this.ServiceType = serviceType;
            this.ServerOptions = serverOptions;
            this.ImplementationType = implementationType;
        }

        public RpcServerOptions? ServerOptions { get; }

        public Type ServiceType { get; }

        public Type? ImplementationType { get; }
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

    public class RpcServiceRegistration : IRpcServiceRegistration
    {
        public RpcServiceRegistration(Type serviceType)
        {
            this.ServiceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
        }

        public RpcServiceRegistration(Type serviceType, RpcServerOptions? serverOptions)
        {
            this.ServiceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
            this.ServerOptions = serverOptions;
        }

        public RpcServerOptions? ServerOptions { get; }

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

    public class RpcServicesAssemblyRegistration : IRpcServiceRegistration
    {
        public RpcServicesAssemblyRegistration(Assembly servicesAssembly, RpcServerOptions? serverOptions = null)
        {
            this.ServicesAssembly = servicesAssembly;
            this.ServerOptions = serverOptions;
        }

        public RpcServerOptions? ServerOptions { get; }

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
