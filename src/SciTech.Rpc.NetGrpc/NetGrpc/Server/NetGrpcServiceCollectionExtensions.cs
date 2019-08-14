using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SciTech.Rpc.Grpc.Server.Internal;
using SciTech.Rpc.Internal;
using SciTech.Rpc.NetGrpc.Server.Internal;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using System;
using System.Collections.Generic;
using System.Reflection;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.NetGrpc.Server
{
    /// <summary>
    /// A builder abstraction for configuring SciTech.Rpc servers.
    /// </summary>
    public interface IRpcServerBuilder
    {
        /// <summary>
        /// Gets the builder service collection.
        /// </summary>
        IServiceCollection Services { get; }
    }

    /// <summary>
    /// Extension methods for the SciTech.Rpc gRPC services.
    /// </summary>
    public static class NetGrpcServiceCollectionExtensions
    {
        static NetGrpcServiceCollectionExtensions()
        {
            RpcServiceCollectionExtensions.ServiceRegistered += RpcServiceCollectionExtensions_ServiceRegistered;
        }

        /// <summary>
        /// Adds SciTech.Rpc gRPC services to the specified <see cref="IServiceCollection" />.
        /// </summary>
        /// <remarks>NOTE. This method tries to register common RPC services, like <see cref="IRpcServiceDefinitionBuilder"/>
        /// and <see cref="IRpcServicePublisher"/>. To provide specific implementations of these interfaces, add them 
        /// to the service collection prior to calling this method.
        /// </remarks>
        /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
        /// <returns>An <see cref="IServiceCollection"/> that can be used to further configure services.</returns>
        public static IRpcServerBuilder AddNetGrpc(this IServiceCollection services)
        {
            services.AddGrpc();

            services.TryAddSingleton<NetGrpcServer>();

            services.TryAddTransient<IServiceMethodProvider<NetGrpcServer>, RpcCoreServiceMethodProvider>();

            services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IServiceMethodProvider<>), typeof(NetGrpcServiceMethodProvider<>)));
            services.TryAdd(ServiceDescriptor.Scoped(typeof(NetGrpcServiceActivator<>), typeof(NetGrpcServiceActivator<>)));
            services.AddTransient(typeof(NetGrpcServiceStubBuilder<>));

            services.TryAddSingleton<RpcServiceDefinitionBuilder>();
            services.TryAddSingleton<IRpcServiceDefinitionBuilder>(s => s.GetRequiredService<RpcServiceDefinitionBuilder>());
            services.TryAddSingleton<IRpcServiceDefinitionsProvider>(s => s.GetRequiredService<RpcServiceDefinitionBuilder>());

            services.TryAddSingleton(s => new RpcServicePublisher(s.GetRequiredService<IRpcServiceDefinitionsProvider>()));
            services.TryAddSingleton<IRpcServicePublisher>(s => s.GetRequiredService<RpcServicePublisher>());
            services.TryAddSingleton<IRpcServiceActivator>(s => s.GetRequiredService<RpcServicePublisher>());

            return new RpcServerBuilder(services);
        }

        /// <summary>
        /// Adds SciTech.Rpc gRPC services to the specified <see cref="IServiceCollection" />.
        /// </summary>
        /// <inheritdoc/>
        /// <param name="options"></param>
        /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
        /// <returns>An <see cref="IServiceCollection"/> that can be used to further configure services.</returns>
        public static IRpcServerBuilder AddNetGrpc(this IServiceCollection services, Action<RpcServerOptions> options)
        {
            return services.Configure(options).AddNetGrpc();
        }

        private static void RpcServiceCollectionExtensions_ServiceRegistered(object? sender, ServiceRegistrationEventArgs e)
        {
            List<RpcServiceInfo> allServices = RpcBuilderUtil.GetAllServices(e.ServiceType, RpcServiceDefinitionSide.Server, true);
            foreach (var rpcService in allServices)
            {
                e.Services.TryAddEnumerable(ServiceDescriptor.Singleton(
                    typeof(IConfigureOptions<>).MakeGenericType(typeof(GrpcServiceOptions<>).MakeGenericType(typeof(NetGrpcServiceActivator<>).MakeGenericType(rpcService.Type))),
                    typeof(NetGrpcServiceActivatorConfig<>).MakeGenericType(rpcService.Type)));
            }
        }
    }

#pragma warning disable CA1812 // Internal class is apparently never instantiated.
    internal class NetGrpcServiceMethodProvider<TActivator> : IServiceMethodProvider<TActivator> where TActivator : class
#pragma warning restore CA1812 // Internal class is apparently never instantiated.
    {
        private static MethodInfo BuildServiceStubMethod =
            typeof(NetGrpcServiceMethodProvider<TActivator>)
            .GetMethod(nameof(BuildServiceStub), BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new NotImplementedException(
                $"{nameof(BuildServiceStub)} not correctly implemented on {nameof(NetGrpcServiceMethodProvider<TActivator>)}");
        //private RpcServiceOptions options;

        private NetGrpcServer rpcServer;

        private IServiceProvider serviceProvider;

        public NetGrpcServiceMethodProvider(IServiceProvider serviceProvider)
        {
            this.rpcServer = serviceProvider.GetRequiredService<NetGrpcServer>();
            this.serviceProvider = serviceProvider;
            //this.options = options.Value;
        }

        public void OnServiceMethodDiscovery(ServiceMethodProviderContext<TActivator> context)
        {
            var activatorType = typeof(TActivator);
            if (activatorType.IsGenericType &&
                activatorType.GetGenericTypeDefinition().Equals(typeof(NetGrpcServiceActivator<>)))
            {
                var serviceType = activatorType.GetGenericArguments()[0];

                var typedBuildServiceStubMethod = BuildServiceStubMethod.MakeGenericMethod(serviceType);
                typedBuildServiceStubMethod.Invoke(this, new object[] { context });
            }
        }

        private void BuildServiceStub<TService>(ServiceMethodProviderContext<TActivator> context) where TService : class
        {
            var typedContext = context as ServiceMethodProviderContext<NetGrpcServiceActivator<TService>>;
            if (typedContext == null)
            {
                // This shouldn't happen, since OnServiceMethodDiscovery should have already checked the type
                throw new InvalidCastException("Unexpected failure when casting to ServiceMethodProviderContext<NetGrpcServiceActivator<TService>>.");
            }

            var stubBuilder = this.serviceProvider.GetService<NetGrpcServiceStubBuilder<TService>>();
            if (stubBuilder != null)
            {
                stubBuilder.Bind(typedContext);
            }
            else
            {
                // TODO: What now? Log warning or throw exception?
            }
        }
    }

#pragma warning disable CA1812 // Internal class is apparently never instantiated.
    internal class RpcCoreServiceMethodProvider : IServiceMethodProvider<NetGrpcServer>
#pragma warning restore CA1812 // Internal class is apparently never instantiated.
    {
        private readonly NetGrpcServer server;

        public RpcCoreServiceMethodProvider(NetGrpcServer server)
        {
            this.server = server;
        }

        public void OnServiceMethodDiscovery(ServiceMethodProviderContext<NetGrpcServer> context)
        {
            context.AddUnaryMethod(GrpcMethodDefinition.Create<RpcObjectRequest, RpcServicesQueryResponse>(
                GrpcCore.MethodType.Unary,
                "SciTech.Rpc.RpcService", "QueryServices",
                this.server.Serializer),
                new List<object>(),
                NetGrpcServer.QueryServices);// (s, request, context) => Task.FromResult(s.QueryServices(request)));
        }
    }

    internal class RpcServerBuilder : IRpcServerBuilder
    {
        internal RpcServerBuilder(IServiceCollection services)
        {
            this.Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public IServiceCollection Services { get; }
    }

    public static class RpcServerBuilderExtensions
    {
        /// <summary>
        /// Adds service specific options to an <see cref="IRpcServerBuilder"/>.
        /// </summary>
        /// <typeparam name="TService">The service type to configure.</typeparam>
        /// <param name="rpcBuilder">The <see cref="IGrpcServerBuilder"/>.</param>
        /// <param name="configure">A callback to configure the service options.</param>
        /// <returns>The same instance of the <see cref="IRpcServerBuilder"/> for chaining.</returns>
        public static IRpcServerBuilder AddServiceOptions<TService>(this IRpcServerBuilder rpcBuilder,
            Action<RpcServiceOptions<TService>> configure) where TService : class
        {
            if (rpcBuilder == null)
            {
                throw new ArgumentNullException(nameof(rpcBuilder));
            }

            // GetServiceInfoFromType will throw if TService is not an RPC service (replace with an IsRpcService method)
            RpcBuilderUtil.GetServiceInfoFromType(typeof(TService));

            rpcBuilder.Services.Configure(configure);

            RpcServiceCollectionExtensions.NotifyServiceRegistered<TService>(rpcBuilder.Services);
            return rpcBuilder;
        }
    }

    /// <summary>
    /// Provides extension methods that can be user to register types and services that
    /// used by SciTech RPC services and RPC serialization.
    /// </summary>
    public static class RpcServiceCollectionExtensions
    {
        private static MethodInfo ConfigureOptionsMethod =
            typeof(RpcServiceCollectionExtensions)
            .GetMethod(nameof(ConfigureOptions), BindingFlags.Static| BindingFlags.NonPublic)
            ?? throw new NotImplementedException(
                $"{nameof(ConfigureOptions)} not correctly implemented on {nameof(RpcServiceCollectionExtensions)}");
        
        /// <summary>
        /// Invoked when a service type has been registered. Can be used by
        /// RPC server implementations to propagate suitable options to the underlying
        /// communication layer.
        /// </summary>
        public static event EventHandler<ServiceRegistrationEventArgs> ServiceRegistered;

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
        /// <typeparam name="TService">The service interface type. Must be an interface with the <see cref="RpcServiceAttribute"/> applied.</typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
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
        /// <param name="type">The service interface type. Must be an interface with the <see cref="RpcServiceAttribute"/> applied.</param>
        /// <returns>An <see cref="IServiceCollection"/> that can be used to further configure services.</returns>
        public static IServiceCollection RegisterRpcService(this IServiceCollection services, Type type, 
            Action<RpcServerOptions>? configureOptions = null)
        {
            AddServiceRegistration(services, new RpcServiceRegistration(type, null), configureOptions);
            return services;
        }

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
            ServiceRegistered?.Invoke(null,new ServiceRegistrationEventArgs(services, typeof(TService)));
        }

        private static void AddServiceRegistration(IServiceCollection services, IRpcServiceRegistration registration, Action<RpcServerOptions>? configureOptions)
        {
            // Could have been added as transient, since it's only used once during initialization. However,
            // that would cause a factory delegate to be kept in memory, which probably consumes as much memory as 
            // the RpcServiceRegistration instance.
            services.AddSingleton(registration);

            // Avoid getting service types unless someone is interested in the registered services
            // Enumerating services may be slow.
            if (configureOptions != null )
            {
                foreach (var registeredType in registration.GetServiceTypes(RpcServiceDefinitionSide.Server))
                {
                    List<RpcServiceInfo> allServices = RpcBuilderUtil.GetAllServices(registeredType.ServiceType, RpcServiceDefinitionSide.Server, true);
                    foreach (var rpcService in allServices)
                    {
                        var configOptionsMethod = ConfigureOptionsMethod.MakeGenericMethod(rpcService.Type);
                        configOptionsMethod.Invoke(null, new object[] { services, configureOptions });
                        
                        ServiceRegistered?.Invoke(null,new ServiceRegistrationEventArgs(services, rpcService.Type));
                    }
                }
            }
        }
        
        private static void ConfigureOptions<TService>(IServiceCollection services, Action<RpcServerOptions> configureOptions)
        {
            services.Configure(new Action<RpcServiceOptions<TService>>(o=>
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
