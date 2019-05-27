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
    /// TODO: Move non-gRPC to the SciTech.Rpc project (e.g. AddServiceOptions, RegisterKnownType etc.).
    /// </summary>
    public static class NetGrpcServiceCollectionExtensions
    {
        /// <summary>
        /// Adds SciTech.Rpc gRPC services to the specified <see cref="IServiceCollection" />.
        /// </summary>
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

            services.AddSingleton<RpcServiceDefinitionBuilder>()
                .AddSingleton<IRpcServiceDefinitionBuilder>(s => s.GetRequiredService<RpcServiceDefinitionBuilder>())
                .AddSingleton<IRpcServiceDefinitionsProvider>(s => s.GetRequiredService<RpcServiceDefinitionBuilder>());

            services.TryAddSingleton(s => new RpcServicePublisher(s.GetRequiredService<IRpcServiceDefinitionsProvider>()));
            services.TryAddSingleton<IRpcServicePublisher>(s => s.GetRequiredService<RpcServicePublisher>());
            services.TryAddSingleton<IRpcServiceActivator>(s => s.GetRequiredService<RpcServicePublisher>());

            return new RpcServerBuilder(services);
        }

        /// <summary>
        /// Adds SciTech.Rpc gRPC services to the specified <see cref="IServiceCollection" />.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
        /// <returns>An <see cref="IServiceCollection"/> that can be used to further configure services.</returns>
        public static IRpcServerBuilder AddNetGrpc(this IServiceCollection services, Action<RpcServiceOptions> options)
        {
            return services.Configure(options).AddNetGrpc();
        }

        /// <summary>
        /// Adds service specific options to an <see cref="IRpcServerBuilder"/>.
        /// </summary>
        /// <typeparam name="TService">The service type to configure.</typeparam>
        /// <param name="rpcBuilder">The <see cref="IGrpcServerBuilder"/>.</param>
        /// <param name="configure">A callback to configure the service options.</param>
        /// <returns>The same instance of the <see cref="IRpcServerBuilder"/> for chaining.</returns>
        public static IRpcServerBuilder AddServiceOptions<TService>(this IRpcServerBuilder rpcBuilder, Action<RpcServiceOptions<TService>> configure) where TService : class
        {
            if (rpcBuilder == null)
            {
                throw new ArgumentNullException(nameof(rpcBuilder));
            }

            rpcBuilder.Services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IConfigureOptions<GrpcServiceOptions<NetGrpcServiceActivator<TService>>>), typeof(NetGrpcServiceActivatorConfig<TService>)));

            rpcBuilder.Services.Configure(configure);
            return rpcBuilder;
        }

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
        public static IServiceCollection RegisterKnownType(this IServiceCollection builder, Type type)
        {
            builder.AddSingleton(new KnownSerializationType(type));
            return builder;
        }

        /// <summary>
        /// Registers an RPC service interface that could be used to implement an RPC service.
        /// </summary>
        /// <typeparam name="TService">The service interface type. Must be an interface with the <see cref="RpcServiceAttribute"/> applied.</typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
        /// <returns>An <see cref="IServiceCollection"/> that can be used to further configure services.</returns>
        public static IServiceCollection RegisterRpcService<TService>(this IServiceCollection services, RpcServiceOptions? options = null) where TService : class
        {
            // Could have been added as transient, since it's only used once during initialization. However,
            // that would cause a factory delegate to be kept in memory, which probably consumes as much as the RpcServiceRegistration
            // instance.
            services.AddSingleton<IRpcServiceRegistration>(new RpcServiceRegistration(typeof(TService), options));
            return services;
        }

        /// <summary>
        /// Registers an RPC service interface that could be used to implement an RPC service.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
        /// <param name="type">The service interface type. Must be an interface with the <see cref="RpcServiceAttribute"/> applied.</param>
        /// <returns>An <see cref="IServiceCollection"/> that can be used to further configure services.</returns>
        public static IServiceCollection RegisterRpcService(this IServiceCollection services, Type type, RpcServiceOptions? options = null)
        {
            // See RegisterRpcService<TService>
            services.AddSingleton<IRpcServiceRegistration>(new RpcServiceRegistration(type, options));
            return services;
        }

        public static IServiceCollection RegisterRpcServicesAssembly(this IServiceCollection builder, Assembly assembly, RpcServiceOptions? options = null)
        {
            // See RegisterRpcService<TService>
            builder.AddSingleton<IRpcServiceRegistration>(new RpcServicesAssemblyRegistration(assembly, options));
            return builder;
        }
    }

    internal class NetGrpcServiceMethodProvider<TActivator> : IServiceMethodProvider<TActivator> where TActivator : class
    {
        private static MethodInfo BuildServiceStubMethod = typeof(NetGrpcServiceMethodProvider<TActivator>).GetMethod(nameof(BuildServiceStub), BindingFlags.Instance | BindingFlags.NonPublic);
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
                // This shouldn't happen, since OnServiceMethodDiscover should have already checked the type
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

    internal class RpcCoreServiceMethodProvider : IServiceMethodProvider<NetGrpcServer>
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
}
