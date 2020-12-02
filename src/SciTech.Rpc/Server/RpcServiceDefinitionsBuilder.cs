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
using SciTech.Rpc.Serialization;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;

namespace SciTech.Rpc.Server
{
    /// <summary>
    /// Default implementation of <see cref="IRpcServiceDefinitionsBuilder"/> (and <see cref="IRpcServiceDefinitionsProvider"/>). 
    /// </summary>
    public class RpcServiceDefinitionsBuilder : IRpcServiceDefinitionsBuilder
    {
        private readonly List<IRpcServerExceptionConverter> registeredExceptionConverters = new List<IRpcServerExceptionConverter>();

        private readonly Dictionary<string, Type> registeredServices = new Dictionary<string, Type>();

        private readonly Dictionary<Type, RegisteredServiceType> registeredServiceTypes = new Dictionary<Type, RegisteredServiceType>();

        private readonly object syncRoot = new object();

        private RpcServerFaultHandler? customFaultHandler;

        private ImmutableArray<IRpcServerExceptionConverter> exceptionConverters;

        private bool isFrozen;

        private IImmutableList<Type>? registeredServicesList;

        private struct RegisteredServiceType
        {
            internal Type? ImplementationType;
            internal IRpcServerOptions? Options;

            internal RegisteredServiceType(Type? implementationType, IRpcServerOptions? options)
            {
                this.ImplementationType = implementationType;
                this.Options = options;
            }
        }

        public RpcServiceDefinitionsBuilder(
            IEnumerable<IRpcServiceRegistration>? serviceRegistrations = null)
        {
            this.Options = /*options?.AsImmutable() ?? */ImmutableRpcServerOptions.Empty;

            if (serviceRegistrations != null)
            {
                foreach (var registration in serviceRegistrations)
                {
                    foreach (var registeredType in registration.GetServiceTypes(RpcServiceDefinitionSide.Server))
                    {
                        this.RegisterService(registeredType.ServiceType, registeredType.ImplementationType, registeredType.ServerOptions);
                    }
                }
            }
        }

        public event EventHandler<RpcServicesEventArgs>? ServicesRegistered;

        public ImmutableArray<RpcServerCallInterceptor> CallInterceptors { get; } = ImmutableArray<RpcServerCallInterceptor>.Empty;

        public RpcServerFaultHandler? CustomFaultHandler
        {
            get
            {
                lock (this.syncRoot)
                {
                    if (this.customFaultHandler == null && this.registeredExceptionConverters.Count > 0)
                    {
                        this.customFaultHandler = new RpcServerFaultHandler(null, this.registeredExceptionConverters, null);
                    }

                    return this.customFaultHandler;
                }
            }
        }

        public ImmutableArray<IRpcServerExceptionConverter> ExceptionConverters
        {
            get
            {
                lock (this.syncRoot)
                {
                    if (this.exceptionConverters.IsDefault)
                    {
                        this.exceptionConverters = this.registeredExceptionConverters.ToImmutableArray();
                    }

                    return this.exceptionConverters;
                }
            }
        }

        public bool IsFrozen => this.isFrozen;

        public ImmutableRpcServerOptions Options { get;  }

        public IRpcSerializer? Serializer => this.Options.Serializer;

        public void Freeze()
        {
            this.isFrozen = true;
        }

        public RpcServiceInfo? GetRegisteredServiceInfo(Type type)
        {
            if( this.registeredServiceTypes.TryGetValue( type, out var registration ))
            {
                return RpcBuilderUtil.GetServiceInfoFromType(type, registration.ImplementationType);
            }

            return null;
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

        public IRpcServerOptions? GetServiceOptions(Type serviceType)
        {
            if (serviceType is null) throw new ArgumentNullException(nameof(serviceType));

            lock (this.syncRoot)
            {
                this.registeredServiceTypes.TryGetValue(serviceType, out var registeredServiceType);
                return registeredServiceType.Options;
            }
        }

        public bool IsServiceRegistered(Type serviceType)
        {
            lock (this.syncRoot)
            {
                return this.registeredServiceTypes.ContainsKey(serviceType);
            }
        }

        public IRpcServiceDefinitionsBuilder RegisterAssemblyServices(params Assembly[] assemblies)
        {
            if (assemblies != null)
            {
                foreach (var assembly in assemblies)
                {
                    foreach (var type in assembly.ExportedTypes)
                    {
                        if (type.IsInterface)
                        {
                            var rpcServiceAttribute = type.GetCustomAttribute<RpcServiceAttribute>(false);
                            if (rpcServiceAttribute != null && rpcServiceAttribute.ServiceDefinitionSide != RpcServiceDefinitionSide.Client)
                            {
                                this.RegisterService(type);
                            }
                        }
                    }
                }
            }

            return this;
        }

        public IRpcServiceDefinitionsBuilder RegisterExceptionConverter(IRpcServerExceptionConverter exceptionConverter)
        {
            lock (this.syncRoot)
            {
                this.registeredExceptionConverters.Add(exceptionConverter);
                this.customFaultHandler = null;
                this.exceptionConverters = default;
            }

            return this;
        }

        public IRpcServiceDefinitionsBuilder RegisterImplementation(Type implementationType, IRpcServerOptions? options = null)
        {
            var allServices = RpcBuilderUtil.GetAllServices(implementationType, true);
            foreach (var serviceInfo in allServices)
            {
                this.RegisterService(serviceInfo.Type, implementationType, options);
            }

            return this;
        }

        public IRpcServiceDefinitionsBuilder RegisterService(Type serviceType, Type? implementationType = null, IRpcServerOptions? options = null)
        {
            if (serviceType is null) throw new ArgumentNullException(nameof(serviceType));
            if (implementationType != null && !serviceType.IsAssignableFrom(implementationType))
            {
                throw new ArgumentException("Implementation type must implement service type.", nameof(implementationType));
            }


            this.CheckFrozen();

            List<RpcServiceInfo> allServices = RpcBuilderUtil.GetAllServices(serviceType, implementationType, RpcServiceDefinitionSide.Server, false);

            var newServices = new List<RpcServiceInfo>();
            Type[]? newServiceTypes = null;
            lock (this.syncRoot)
            {
                foreach (var service in allServices)
                {
                    if (!this.registeredServiceTypes.TryGetValue(service.Type, out var currRegistration ))
                    {
                        this.registeredServiceTypes.Add(service.Type, new RegisteredServiceType( implementationType, options ));

                        if (this.registeredServices.TryGetValue(service.FullName, out var existingServiceType))
                        {
                            if (!service.Type.Equals(existingServiceType))
                            {
                                // TODO: This should be allowed, as long as the service operations and options don't collide.
                                throw new RpcDefinitionException($"Service '{service.FullName}' already registered using the interface '{existingServiceType}'");
                            }

                            // TODO: This will no longer happen, since registeredServiceTypes check will prevent it from getting here.
                            continue;
                        }
                        else
                        {
                            newServices.Add(service);
                        }
                    }
                    else
                    {
                        if( currRegistration.ImplementationType != implementationType)
                        {
                            throw new RpcDefinitionException($"Service '{serviceType}' already registered with a different implementation type.");
                        }

                        // Type already registered, but let's update the service options if provided.
                        if (options != null)
                        {
                            this.registeredServiceTypes[service.Type] = new RegisteredServiceType(implementationType, options);
                        }

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

        private void CheckFrozen()
        {
            if (this.isFrozen)
            {
                throw new InvalidOperationException("Cannot register services to a frozen service registrator.");
            }
        }
    }
}
