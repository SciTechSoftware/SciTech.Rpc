#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SciTech.Rpc.Client.Options;
using System;
using MSOptions = Microsoft.Extensions.Options.Options;

namespace SciTech.Rpc.Client
{
    public static class ServiceCollectionsExtensions
    {

        /// <summary>
        /// Configures <see cref="IRpcClientOptions"/> so that all registered <see cref="IRpcClientExceptionConverter"/>s
        /// are added to the <see cref="IRpcClientOptions.ExceptionConverters"/> collection of all option instances.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
        /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
        public static IServiceCollection ConfigureAllRpcClientExceptionConverters(this IServiceCollection services)
        {
            services.AddTransient<IConfigureNamedOptions<RpcClientOptions>>(
                (s) => new ConfigureRpcClientExceptionConverters(null, s.GetServices<IRpcClientExceptionConverter>()));
            return services;
        }

        /// <summary>
        /// Configures <see cref="IRpcClientOptions"/> so that all registered <see cref="IRpcClientExceptionConverter"/>s
        /// are added to the <see cref="IRpcClientOptions.ExceptionConverters"/> collection of the default options instance.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
        /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
        public static IServiceCollection ConfigureRpcClientExceptionConverters(this IServiceCollection services)
            => services.ConfigureRpcClientExceptionConverters(MSOptions.DefaultName);

        /// <summary>
        /// Configures <see cref="IRpcClientOptions"/> so that all registered <see cref="IRpcClientExceptionConverter"/>s
        /// are added to the <see cref="IRpcClientOptions.ExceptionConverters"/> collection of the named options instance.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
        /// <param name="name">The name of the options instance.</param>
        /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
        public static IServiceCollection ConfigureRpcClientExceptionConverters(this IServiceCollection services, string name)
        {
            services.AddTransient<IConfigureNamedOptions<RpcClientOptions>>(
                (s) => new ConfigureRpcClientExceptionConverters(name, s.GetServices<IRpcClientExceptionConverter>()));
            return services;
        }
    }
}
