using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SciTech.Rpc.NetGrpc.Server.Internal;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using System;
using System.Reflection;

namespace SciTech.Rpc.NetGrpc.Server
{
    /// <summary>
    /// Extension methods for the SciTech.Rpc gRPC services.
    /// </summary>
    public static class NetGrpcServiceCollectionExtensions
    {
        /// <summary>
        /// Adds SciTech.Rpc gRPC services to the specified <see cref="IServiceCollection" />.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
        /// <returns>An <see cref="IServiceCollection"/> that can be used to further configure services.</returns>
        public static IServiceCollection AddNetGrpc(this IServiceCollection services, Action<RpcServiceOptions>? options = null)
        {
            services.AddGrpc();

            services.AddSingleton<IConfigureOptions<GrpcServiceOptions<NetGrpcServiceActivator>>, NetGrpcServiceActivatorConfig>();

            if (options != null)
            {
                services.Configure<RpcServiceOptions>(options)
                    .AddSingleton<IServiceMethodProvider<NetGrpcServiceActivator>, NetGrpcServiceMethodProvider>();
            }
            else
            {
                services.AddSingleton<IServiceMethodProvider<NetGrpcServiceActivator>, NetGrpcServiceMethodProvider>();

            }

            services.AddSingleton<RpcServiceDefinitionBuilder>()
                .AddSingleton<IRpcServiceDefinitionBuilder>(s => s.GetRequiredService<RpcServiceDefinitionBuilder>())
                .AddSingleton<IRpcServiceDefinitionsProvider>(s => s.GetRequiredService<RpcServiceDefinitionBuilder>());

            services.TryAddSingleton(s => new RpcServicePublisher(s.GetRequiredService<IRpcServiceDefinitionsProvider>()));
            services.TryAddSingleton<IRpcServicePublisher>(s => s.GetRequiredService<RpcServicePublisher>());
            services.TryAddSingleton<IRpcServiceActivator>(s => s.GetRequiredService<RpcServicePublisher>());

            services.AddScoped(s => new NetGrpcServiceActivator(s));

            return services;
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
        public static IServiceCollection RegisterRpcService<TService>(this IServiceCollection services) where TService : class
        {
            services.AddSingleton<IRpcServiceRegistration>(new RpcServiceRegistration(typeof(TService)));
            return services;
        }

        /// <summary>
        /// Registers an RPC service interface that could be used to implement an RPC service.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
        /// <param name="type">The service interface type. Must be an interface with the <see cref="RpcServiceAttribute"/> applied.</param>
        /// <returns>An <see cref="IServiceCollection"/> that can be used to further configure services.</returns>
        public static IServiceCollection RegisterRpcService(this IServiceCollection services, Type type)
        {
            services.AddSingleton<IRpcServiceRegistration>(new RpcServiceRegistration(type));
            return services;
        }

        public static IServiceCollection RegisterRpcServicesAssembly(this IServiceCollection builder, Assembly assembly)
        {
            builder.AddSingleton<IRpcServiceRegistration>(new RpcServicesAssemblyRegistration(assembly));
            return builder;
        }
    }

    internal class NetGrpcServiceMethodProvider : IServiceMethodProvider<NetGrpcServiceActivator>
    {
        private readonly RpcServicePublisher servicePublisher;

        private RpcServiceOptions options;

        public NetGrpcServiceMethodProvider(RpcServicePublisher servicePublisher, IOptions<RpcServiceOptions> options)
        {
            this.servicePublisher = servicePublisher;
            this.options = options.Value;
        }

        public void OnServiceMethodDiscovery(ServiceMethodProviderContext<NetGrpcServiceActivator> context)
        {
            var rpcServer = new NetGrpcServer(this.servicePublisher, context, this.options);
            rpcServer.Start();

        }
    }
}
