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

using SciTech.Collections;
using SciTech.Rpc.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;

namespace SciTech.Rpc.Server
{
    public class RpcServiceDefinitionBuilder : IRpcServiceDefinitionBuilder
    {
        private readonly List<IRpcServerExceptionConverter> registeredExceptionConverters = new List<IRpcServerExceptionConverter>();

        private readonly HashSet<Type> registeredServiceTypes = new HashSet<Type>();

        private readonly Dictionary<string, Type> registeredServices = new Dictionary<string, Type>();

        private readonly object syncRoot = new object();

        private RpcServerFaultHandler? customFaultHandler;

        private bool isFrozen;

        private IImmutableList<Type>? registeredServicesList;

        public RpcServiceDefinitionBuilder(IEnumerable<IRpcServiceRegistration>? serviceRegistrations = null, IEnumerable<IRpcServerExceptionConverter>? exceptionConverters = null)
        {
            if (serviceRegistrations != null)
            {
                foreach (var registration in serviceRegistrations)
                {
                    foreach (var serviceType in registration.GetServiceTypes(RpcServiceDefinitionType.Server))
                    {
                        this.RegisterService(serviceType);
                    }
                }
            }

            if (exceptionConverters != null)
            {
                foreach (var exceptionConverter in exceptionConverters)
                {
                    this.RegisterExceptionConverter(exceptionConverter);
                }
            }
        }

        public event EventHandler<RpcServicesEventArgs> ServicesRegistered;

        public RpcServerFaultHandler? CustomFaultHandler
        {
            get
            {
                lock (this.syncRoot)
                {
                    if (this.customFaultHandler == null && this.registeredExceptionConverters.Count > 0)
                    {
                        this.customFaultHandler = new RpcServerFaultHandler(this.registeredExceptionConverters);
                    }

                    return this.customFaultHandler;
                }
            }
        }

        public bool IsFrozen => this.isFrozen;

        public void Freeze()
        {
            this.isFrozen = true;
        }

        public IImmutableList<Type> GetRegisteredServiceTypes()
        {
            lock (this.syncRoot)
            {
                if (this.registeredServicesList == null)
                {
                    this.registeredServicesList = this.registeredServices.Values.AsImmutableArrayList();
                }

                return this.registeredServicesList;
            }
        }

        public bool IsServiceRegistered(Type serviceType)
        {
            lock (this.syncRoot)
            {
                return this.registeredServiceTypes.Contains(serviceType);
            }
        }

        public IRpcServiceDefinitionBuilder RegisterAssemblyServices(params Assembly[] assemblies)
        {
            foreach (var assembly in assemblies)
            {
                foreach (var type in assembly.ExportedTypes)
                {
                    if (type.IsInterface)
                    {
                        var rpcServiceAttribute = type.GetCustomAttribute<RpcServiceAttribute>(false);
                        if (rpcServiceAttribute != null && rpcServiceAttribute.ServiceDefinitionType != RpcServiceDefinitionType.Client)
                        {
                            this.RegisterService(type);
                        }
                    }
                }
            }

            return this;
        }

        public IRpcServiceDefinitionBuilder RegisterExceptionConverter(IRpcServerExceptionConverter exceptionConverter)
        {
            lock (this.syncRoot)
            {
                this.registeredExceptionConverters.Add(exceptionConverter);
                this.customFaultHandler = null;
            }

            return this;
        }

        public IRpcServiceDefinitionBuilder RegisterService<TService>()
        {
            return this.RegisterService(typeof(TService));
        }

        public IRpcServiceDefinitionBuilder RegisterService(Type serviceType)
        {
            this.CheckFrozen();

            List<RpcServiceInfo> allServices = RpcBuilderUtil.GetAllServices(serviceType, RpcServiceDefinitionType.Server, false);

            var newServices = new List<RpcServiceInfo>();
            Type[]? newServiceTypes = null;
            lock (this.syncRoot)
            {
                foreach (var service in allServices)
                {
                    if (this.registeredServiceTypes.Add(service.Type))
                    {

                        if (this.registeredServices.TryGetValue(service.FullName, out var existingServiceType))
                        {
                            if (!service.Type.Equals(existingServiceType))
                            {
                                // TODO: This should be allowed, as long as the service operations don't collide.
                                throw new RpcDefinitionException($"Service '{service.FullName}' already registered using the interface '{existingServiceType}'");
                            }

                            // TODO: This will no longer happen, since registeredServiceTypes check will prevent it from getting here.
                            continue;
                        }
                        else
                        {
                            newServices.Add(service);
                        }
                    } else
                    {
                        // Type already registered
                        continue;
                    }
                }

                if (newServices.Count > 0)
                {
                    newServiceTypes = new Type[newServices.Count];
                    int di = 0;
                    foreach (var service in newServices)
                    {
                        newServiceTypes[di++] = service.Type;
                        this.registeredServices.Add(service.FullName, service.Type);
                    }
                }
            }

            if (newServiceTypes != null && newServiceTypes.Length > 0)
            {
                this.ServicesRegistered?.Invoke(this, new RpcServicesEventArgs(newServiceTypes));
            }

            return this;
        }

        public IRpcServiceDefinitionBuilder RegisterSingletonService<TService>()
        {
            // TODO: Mark it as singleton?
            return this.RegisterService(typeof(TService));
        }

        private void CheckFrozen()
        {
            if (this.isFrozen)
            {
                throw new InvalidOperationException("Cannot register services to a frozen service registrator.");
            }
        }
    }
}
