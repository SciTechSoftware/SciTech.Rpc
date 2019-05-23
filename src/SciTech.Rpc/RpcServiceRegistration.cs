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
using System.Reflection;

namespace SciTech.Rpc
{
    public interface IRpcServiceRegistration
    {
        /// <summary>
        /// Gets a collection of interface types that implement RPC services.
        /// </summary>
        IEnumerable<Type> GetServiceTypes(RpcServiceDefinitionType definitionType);
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

        public Type ServiceType { get; }

        public IEnumerable<Type> GetServiceTypes(RpcServiceDefinitionType definitionType)
        {
            var rpcServiceAttribute = this.ServiceType.GetCustomAttribute<RpcServiceAttribute>(false);
            if (rpcServiceAttribute != null
                && (definitionType == RpcServiceDefinitionType.Both
                || rpcServiceAttribute.ServiceDefinitionType == RpcServiceDefinitionType.Both
                || definitionType == rpcServiceAttribute.ServiceDefinitionType))
            {
                yield return this.ServiceType;
            }
        }
    }

    public class RpcServicesAssemblyRegistration : IRpcServiceRegistration
    {
        public RpcServicesAssemblyRegistration(Assembly servicesAssembly)
        {
            this.ServicesAssembly = servicesAssembly;
        }

        public Assembly ServicesAssembly { get; }

        public IEnumerable<Type> GetServiceTypes(RpcServiceDefinitionType definitionType)
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
                        yield return type;
                    }
                }
            }
        }
    }
}
