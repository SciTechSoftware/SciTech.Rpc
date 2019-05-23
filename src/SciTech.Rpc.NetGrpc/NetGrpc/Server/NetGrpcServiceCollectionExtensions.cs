using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        public static void AddNetGrpc(this IServiceCollection services)
        {
            services.AddGrpc();

            services.AddSingleton(s => new RpcServiceDefinitionBuilder(s.GetServices<IRpcServiceRegistration>(), s.GetServices<IRpcServerExceptionConverter>()))
                .AddSingleton<IRpcServiceDefinitionBuilder>(s => s.GetRequiredService<RpcServiceDefinitionBuilder>())
                .AddSingleton<IRpcServiceDefinitionsProvider>(s => s.GetRequiredService<RpcServiceDefinitionBuilder>());

            services.TryAddSingleton(s => new RpcServicePublisher(s.GetRequiredService<IRpcServiceDefinitionsProvider>()));
            services.TryAddSingleton<IRpcServicePublisher>(s => s.GetRequiredService<RpcServicePublisher>());
            services.TryAddSingleton<IRpcServiceActivator>(s => s.GetRequiredService<RpcServicePublisher>());

            services.AddScoped(s => new NetGrpcServiceActivator(s));
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
