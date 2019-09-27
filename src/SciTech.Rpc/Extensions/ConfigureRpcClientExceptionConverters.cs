using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SciTech.Collections;
using SciTech.Rpc.Client;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace SciTech.Rpc.Extensions
{
    public class ConfigureRpcClientExceptionConverters : IConfigureNamedOptions<RpcClientOptions>
    {
        private readonly IImmutableList<IRpcClientExceptionConverter> exceptionConverters;
        private readonly string? name;

        public ConfigureRpcClientExceptionConverters(string? name, IEnumerable<IRpcClientExceptionConverter> exceptionConverters)
        {
            this.name = name;
            this.exceptionConverters = exceptionConverters.AsImmutableArrayList();
        }

        public void Configure(RpcClientOptions options)
        {
            Configure(Options.DefaultName, options);
        }

        public void Configure(string name, RpcClientOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            if (this.name == null || this.name == name)
            {
                options.ExceptionConverters.AddRange(this.exceptionConverters);
            }
        }
    }

    public static class RpcServiceCollectionExtensions
    {
        /// <summary>
        /// Configures <see cref="IRpcClientOptions"/> so that all registered <see cref="IRpcClientExceptionConverter"/>s
        /// are added to the <see cref="IRpcClientOptions.ExceptionConverters"/> collection of the default options instance.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
        /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
        public static IServiceCollection ConfigureRpcClientExceptionConverters(this IServiceCollection services)
            => services.ConfigureRpcClientExceptionConverters(Options.DefaultName);

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
    }
}
