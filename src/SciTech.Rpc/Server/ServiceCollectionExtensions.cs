using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SciTech.Rpc.Server.Internal;
using System;
using System.Collections.Generic;
using System.Text;

namespace SciTech.Rpc.Server
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// <para>
        /// Adds SciTech.Rpc server services to the specified <see cref="IServiceCollection" />.
        /// </para>
        /// <para>
        /// NOTE! This method tries to register common RPC services, like <see cref="IRpcServiceDefinitionsBuilder"/>
        /// and <see cref="IRpcServicePublisher"/>. To provide specific implementations of these interfaces, add them 
        /// to the service collection prior to calling this method.
        /// </para>
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
        /// <returns>An <see cref="IServiceCollection"/> that can be used to further configure services.</returns>
        public static IRpcServerBuilder AddRpcServer<TRpcServer>(this IServiceCollection services) where TRpcServer : class, IRpcServer
        {
            services.TryAddSingleton<TRpcServer>();
            services.TryAddSingleton<IRpcServer>(s => s.GetRequiredService<TRpcServer>());
            if( typeof(IRpcServerHost).IsAssignableFrom(typeof(TRpcServer)))
            {
                services.TryAddSingleton(s => (IRpcServerHost)s.GetRequiredService<TRpcServer>());
            }

            services.TryAddSingleton<RpcServiceDefinitionsBuilder>();
            services.TryAddSingleton<IRpcServiceDefinitionsBuilder>(s => s.GetRequiredService<RpcServiceDefinitionsBuilder>());
            services.TryAddSingleton<IRpcServiceDefinitionsProvider>(s => s.GetRequiredService<RpcServiceDefinitionsBuilder>());

            services.TryAddSingleton(
                s => new RpcServicePublisher(s.GetRequiredService<IRpcServiceDefinitionsProvider>(),
                s.GetService<IOptions<RpcServicePublisherOptions>>()?.Value?.ServerId ?? default));
            services.TryAddSingleton<IRpcServicePublisher>(s => s.GetRequiredService<RpcServicePublisher>());
            services.TryAddSingleton<IRpcServiceActivator>(s => s.GetRequiredService<RpcServicePublisher>());

            return new RpcServerBuilder(services);
        }


        ///// <summary>
        ///// Adds SciTech.Rpc gRPC services to the specified <see cref="IServiceCollection" />.
        ///// </summary>
        ///// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
        ///// <returns>An <see cref="IServiceCollection"/> that can be used to further configure services.</returns>

        /// <inheritdoc cref="AddRpcServer{TRpcServer}(IServiceCollection)"/>
        /// <param name="options">The action used to configure the server options.</param>
        public static IRpcServerBuilder AddRpcServer<TRpcServer>(this IServiceCollection services, Action<RpcServerOptions> options)
            where TRpcServer : class, IRpcServer
        {
            return services.Configure(options).AddRpcServer<TRpcServer>();
        }

        public static IServiceCollection AddRpcContextAccessor(this IServiceCollection services)
        {
            services.TryAddSingleton<IRpcContextAccessor>(new RpcContextAccessor());
            return services;
        }
    }
}
