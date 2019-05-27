#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using SciTech.Rpc.Server;
using System;
using System.Collections.Generic;
using System.Text;

namespace SciTech.Rpc.NetGrpc.Server
{
    /// <summary>
    /// Extension methods for publishing an registering RPC services.
    /// </summary>
    public static class NetGrpcApplicationBuilderExtensions
    {
        public static IApplicationBuilder RegisterRpcService<TService>(this IApplicationBuilder builder)
        {
            var definitionsBuilder = builder.ApplicationServices.GetRequiredService<IRpcServiceDefinitionBuilder>();
            definitionsBuilder.RegisterService(typeof(TService));
            return builder;
        }

        /// <summary>
        /// Publishes a singleton service that will be contructed by the <see cref="IServiceProvider"/> associated with the RPC operation.
        /// This method expects that an implementation class has been registered for the RPC service interface type <typeparamref name="TService"/>
        /// </summary>
        /// <typeparam name="TService">The interface type defining the RPC service.</typeparam>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static IApplicationBuilder PublishRpcSingleton<TService>(this IApplicationBuilder builder) where TService : class
        {
            var publisher = builder.ApplicationServices.GetRequiredService<IRpcServicePublisher>();
            publisher.PublishSingleton<TService>();

            return builder;
        }


        /// <summary>
        /// Publishes a singleton service that will be contructed by the <see cref="IServiceProvider"/> associated with the RPC operation.
        /// </summary>
        /// <typeparam name="TServiceImpl">The type implementing the RPC service.</typeparam>
        /// <typeparam name="TService">The interface type defining the RPC service.</typeparam>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static IApplicationBuilder PublishRpcSingleton<TServiceImpl, TService>(this IApplicationBuilder builder)
            where TService : class
            where TServiceImpl : class, TService
        {
            var publisher = builder.ApplicationServices.GetRequiredService<IRpcServicePublisher>();
            publisher.PublishSingleton<TServiceImpl, TService>();

            return builder;

        }

        /// <summary>
        /// Publishes a singleton service factory.
        /// </summary>
        /// <typeparam name="TService">The interface type defining the RPC service.</typeparam>
        /// <param name="builder"></param>
        /// <param name="factory">A factory delegate that should return a service implementation instance.</param>
        /// <returns></returns>
        public static IApplicationBuilder PublishRpcSingleton<TService>(this IApplicationBuilder builder, Func<IServiceProvider, TService> factory)
            where TService : class
        {
            var publisher = builder.ApplicationServices.GetRequiredService<IRpcServicePublisher>();
            publisher.PublishSingleton<TService>(factory);

            return builder;
        }

        public static IApplicationBuilder PublishRpcSingleton<TService>(this IApplicationBuilder builder, TService singletonService, bool takeOwnership = false) where TService : class
        {
            var publisher = builder.ApplicationServices.GetRequiredService<IRpcServicePublisher>();
            publisher.PublishSingleton<TService>(singletonService, takeOwnership);

            return builder;

        }

    }
}
