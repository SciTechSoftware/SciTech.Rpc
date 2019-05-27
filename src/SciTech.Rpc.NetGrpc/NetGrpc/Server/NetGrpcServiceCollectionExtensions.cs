using Grpc.AspNetCore.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using SciTech.Rpc.NetGrpc.Server.Internal;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace SciTech.Rpc.NetGrpc.Server
{

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

            services.AddSingleton<RpcServiceDefinitionBuilder>()
                .AddSingleton<IRpcServiceDefinitionBuilder>(s => s.GetRequiredService<RpcServiceDefinitionBuilder>())
                .AddSingleton<IRpcServiceDefinitionsProvider>(s => s.GetRequiredService<RpcServiceDefinitionBuilder>());

            services.TryAddSingleton(s => new RpcServicePublisher(s.GetRequiredService<IRpcServiceDefinitionsProvider>()));
            services.TryAddSingleton<IRpcServicePublisher>(s => s.GetRequiredService<RpcServicePublisher>());
            services.TryAddSingleton<IRpcServiceActivator>(s => s.GetRequiredService<RpcServicePublisher>());

            services.AddScoped(s => new NetGrpcServiceActivator(s));

            return services;
        }

        public static IServiceCollection RegisterKnownType<T>(this IServiceCollection builder)
        {
            builder.AddSingleton(new KnownSerializationType(typeof(T)));
            return builder;
        }

        public static IServiceCollection RegisterKnownType(this IServiceCollection builder, Type type)
        {
            builder.AddSingleton(new KnownSerializationType(type));
            return builder;
        }

        public static IServiceCollection RegisterRpcService<TService>(this IServiceCollection builder)
        {
            builder.AddSingleton<IRpcServiceRegistration>(new RpcServiceRegistration(typeof(TService)));
            return builder;
        }


        public static IServiceCollection RegisterRpcService(this IServiceCollection builder, Type type)
        {
            builder.AddSingleton<IRpcServiceRegistration>(new RpcServiceRegistration(type));
            return builder;
        }

        public static IServiceCollection RegisterRpcServicesAssembly(this IServiceCollection builder, Assembly assembly)
        {
            builder.AddSingleton<IRpcServiceRegistration>(new RpcServicesAssemblyRegistration(assembly));
            return builder;
        }

    }
}
