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
        /// <summary>
        /// Gets a collection of interface types that implement RPC services.
        /// </summary>
        IEnumerable<RegisteredServiceType> GetServiceTypes(RpcServiceDefinitionType definitionType);
    }

    public struct RegisteredServiceType
    {
        internal RegisteredServiceType(Type serviceType, RpcServiceOptions? serverOptions)
        {
            this.ServiceType = serviceType;
            this.ServerOptions = serverOptions;
        }

        public RpcServiceOptions? ServerOptions { get; }

        public Type ServiceType { get; }
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
        private readonly RpcServiceOptions? serverOptions;

        public RpcServiceRegistration(Type serviceType)
        {
            this.ServiceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
        }

        public RpcServiceRegistration(Type serviceType, RpcServiceOptions? serverOptions)
        {
            this.ServiceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
            this.serverOptions = serverOptions;
        }

        public Type ServiceType { get; }

        public IEnumerable<RegisteredServiceType> GetServiceTypes(RpcServiceDefinitionType definitionType)
        {
            var rpcServiceAttribute = this.ServiceType.GetCustomAttribute<RpcServiceAttribute>(false);
            if (rpcServiceAttribute != null
                && (definitionType == RpcServiceDefinitionType.Both
                || rpcServiceAttribute.ServiceDefinitionType == RpcServiceDefinitionType.Both
                || definitionType == rpcServiceAttribute.ServiceDefinitionType))
            {
                yield return new RegisteredServiceType(this.ServiceType, this.serverOptions);
            }
        }
    }

    public class RpcServicesAssemblyRegistration : IRpcServiceRegistration
    {
        private readonly RpcServiceOptions? serverOptions;

        public RpcServicesAssemblyRegistration(Assembly servicesAssembly, RpcServiceOptions? serverOptions = null)
        {
            this.ServicesAssembly = servicesAssembly;
            this.serverOptions = serverOptions;
        }

        public Assembly ServicesAssembly { get; }

        public IEnumerable<RegisteredServiceType> GetServiceTypes(RpcServiceDefinitionType definitionType)
        {
            foreach (var type in this.ServicesAssembly.ExportedTypes)
            {
                if (type.IsInterface)
                {
                    var rpcServiceAttribute = type.GetCustomAttribute<RpcServiceAttribute>(false);
                    if (rpcServiceAttribute != null
                        && (definitionType == RpcServiceDefinitionType.Both
                        || rpcServiceAttribute.ServiceDefinitionType == RpcServiceDefinitionType.Both
                        || definitionType == rpcServiceAttribute.ServiceDefinitionType))
                    {
                        yield return new RegisteredServiceType(type, this.serverOptions);
                    }
                }
            }
        }
    }
}
