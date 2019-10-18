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

using Microsoft.Extensions.DependencyInjection;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Server;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace SciTech.Rpc
{
    /// <summary>
    /// Provides extension methods that can be user to register types and services that
    /// used by SciTech RPC services and RPC serialization.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        private static MethodInfo ConfigureOptionsMethod =
            typeof(ServiceCollectionExtensions)
            .GetMethod(nameof(ConfigureOptions), BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new NotImplementedException(
                $"{nameof(ConfigureOptions)} not correctly implemented on {nameof(ServiceCollectionExtensions)}");

        /// <summary>
        /// Invoked when a service type has been registered. Can be used by
        /// RPC server implementations to propagate suitable options to the underlying
        /// communication layer.
        /// </summary>
        public static event EventHandler<ServiceRegistrationEventArgs>? ServiceRegistered;

        /// <summary>
        /// Registers a known serializable type that should be available for RPC serializers.
        /// </summary>
        /// <typeparam name="T">The known type.</typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
        /// <returns>An <see cref="IServiceCollection"/> that can be used to further configure services.</returns>
        public static IServiceCollection RegisterKnownType<T>(this IServiceCollection services)
        {
            services.AddSingleton(new KnownSerializationType(typeof(T)));
            return services;
        }

        /// <summary>
        /// Registers a known serializable type that should be available for RPC serializers.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
        /// <param name="type">The known type.</param>
        /// <returns>An <see cref="IServiceCollection"/> that can be used to further configure services.</returns>
        public static IServiceCollection RegisterKnownType(this IServiceCollection services, Type type)
        {
            services.AddSingleton(new KnownSerializationType(type));
            return services;
        }

        /// <summary>
        /// Registers an RPC service interface that could be used to implement an RPC service.
        /// </summary>
        /// <typeparam name="TService">The service interface type. Must be an interface with the <see cref="RpcServiceAttribute"/> 
        /// or the <see cref="ServiceContractAttribute"/> applied.</typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
        /// <param name="configureOptions">Optional delegate that can be used to configure server side options.</param>
        /// <returns>An <see cref="IServiceCollection"/> that can be used to further configure services.</returns>
        public static IServiceCollection RegisterRpcService<TService>(this IServiceCollection services,
            Action<RpcServerOptions>? configureOptions = null) where TService : class
        {
            AddServiceRegistration(services, new RpcServiceRegistration(typeof(TService), null), configureOptions);

            return services;
        }

        /// <summary>
        /// Registers an RPC service interface that could be used to implement an RPC service.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
        /// <param name="type">The service interface type. Must be an interface with the <see cref="RpcServiceAttribute"/> 
        /// or the <see cref="ServiceContractAttribute"/> applied.</param>
        /// <param name="configureOptions">Optional delegate that can be used to configure server side options.</param>
        /// <returns>An <see cref="IServiceCollection"/> that can be used to further configure services.</returns>
        public static IServiceCollection RegisterRpcService(this IServiceCollection services, Type type,
            Action<RpcServerOptions>? configureOptions = null)
        {
            AddServiceRegistration(services, new RpcServiceRegistration(type, null), configureOptions);
            return services;
        }

        /// <summary>
        /// <para>
        /// Registers all RPC service interface that are defined in the specified <paramref name="assembly"/>.
        /// </para>
        /// <para>All exported interface types in the assembly with the <see cref="RpcServiceAttribute"/> applied 
        /// will be registered (types with only the <see cref="ServiceContractAttribute"/> applied will not be registered).
        /// </para>
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
        /// <param name="assembly">The services assembly</param>
        /// <param name="configureOptions">Optional delegate that can be used to configure server side options.</param>
        /// <returns>An <see cref="IServiceCollection"/> that can be used to further configure services.</returns>
        public static IServiceCollection RegisterRpcServicesAssembly(
            this IServiceCollection services,
            Assembly assembly,
            Action<RpcServerOptions>? configureOptions = null)
        {
            AddServiceRegistration(services, new RpcServicesAssemblyRegistration(assembly, null), configureOptions);

            return services;
        }


        internal static void NotifyServiceRegistered<TService>(IServiceCollection services)
        {
            ServiceRegistered?.Invoke(null, new ServiceRegistrationEventArgs(services, typeof(TService)));
        }

        private static void AddServiceRegistration(IServiceCollection services, IRpcServiceRegistration registration, Action<RpcServerOptions>? configureOptions)
        {
            // Could have been added as transient, since it's only used once during initialization. However,
            // that would cause a factory delegate to be kept in memory, which probably consumes as much memory as 
            // the RpcServiceRegistration instance.
            services.AddSingleton(registration);

            // Avoid getting service types unless someone is interested in the registered services
            // Enumerating services may be slow.
            if (configureOptions != null)
            {
                foreach (var registeredType in registration.GetServiceTypes(RpcServiceDefinitionSide.Server))
                {
                    List<RpcServiceInfo> allServices = RpcBuilderUtil.GetAllServices(registeredType.ServiceType, RpcServiceDefinitionSide.Server, true);
                    foreach (var rpcService in allServices)
                    {
                        var configOptionsMethod = ConfigureOptionsMethod.MakeGenericMethod(rpcService.Type);
                        configOptionsMethod.Invoke(null, new object[] { services, configureOptions });

                        ServiceRegistered?.Invoke(null, new ServiceRegistrationEventArgs(services, rpcService.Type));
                    }
                }
            }
        }

        private static void ConfigureOptions<TService>(IServiceCollection services, Action<RpcServerOptions> configureOptions)
        {
            services.Configure(new Action<RpcServiceOptions<TService>>(o =>
            {
                configureOptions(o);
            }));
        }
    }

    public sealed class ServiceRegistrationEventArgs
    {
        public ServiceRegistrationEventArgs(IServiceCollection services, Type serviceType)
        {
            this.Services = services ?? throw new ArgumentNullException(nameof(services));
            this.ServiceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
        }

        /// <summary>
        /// The service collection in which the RPC service has been registered.
        /// </summary>
        public IServiceCollection Services { get; }

        public Type ServiceType { get; }
    }
}
