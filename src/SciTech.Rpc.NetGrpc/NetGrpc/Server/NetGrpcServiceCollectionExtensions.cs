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

using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SciTech.Rpc.Grpc.Internal;
using SciTech.Rpc.Internal;
using SciTech.Rpc.NetGrpc.Server.Internal;
using SciTech.Rpc.Server;
using SciTech.Rpc.Server.Internal;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.ServiceModel;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc.NetGrpc.Server
{
    /// <summary>
    /// Extension methods for the SciTech.Rpc gRPC services.
    /// </summary>
    public static class NetGrpcServiceCollectionExtensions
    {
        static NetGrpcServiceCollectionExtensions()
        {
            ServiceCollectionExtensions.ServiceRegistered += RpcServiceCollectionExtensions_ServiceRegistered;
        }

        /// <summary>
        /// Adds SciTech.Rpc gRPC services to the specified <see cref="IServiceCollection" />.
        /// </summary>
        /// <remarks>NOTE. This method tries to register common RPC services, like <see cref="IRpcServiceDefinitionsBuilder"/>
        /// and <see cref="IRpcServicePublisher"/>. To provide specific implementations of these interfaces, add them 
        /// to the service collection prior to calling this method.
        /// </remarks>
        /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
        /// <returns>An <see cref="IServiceCollection"/> that can be used to further configure services.</returns>
        public static IRpcServerBuilder AddNetGrpc(this IServiceCollection services)
        {
            services.AddGrpc();

            services.TryAddTransient<IServiceMethodProvider<NetGrpcServer>, RpcCoreServiceMethodProvider>();

            // Use reflection to get NetGrpcServiceMethodProvider<> type. For some reason using typeof(NetGrpcServiceMethodProvider<>)
            // causes a BadImageFormatException in .NET Core 3.0 Preview 9. Hopefully this hack can be removed soon.
            var providerType = typeof(NetGrpcServiceCollectionExtensions).Assembly.GetType("SciTech.Rpc.NetGrpc.Server.NetGrpcServiceMethodProvider`1");
            //var providerType = typeof(NetGrpcServiceMethodProvider<>);

            services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IServiceMethodProvider<>), providerType));

            services.TryAdd(ServiceDescriptor.Scoped(typeof(NetGrpcServiceActivator<>), typeof(NetGrpcServiceActivator<>)));

            return services.AddRpcServer<NetGrpcServer>();
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
    public class NetGrpcServiceMethodProvider<TActivator> : IServiceMethodProvider<TActivator> where TActivator : class
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

                //MethodInfo buildServiceStubMethod = typeof(NetGrpcServiceMethodProvider<TActivator>)
                //    .GetMethod(nameof(BuildServiceStub), BindingFlags.Instance | BindingFlags.NonPublic)!;

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

            var serviceInfo = this.rpcServer.ServiceDefinitionsProvider.GetRegisteredServiceInfo(typeof(TService));
            if( serviceInfo == null )
            {
                throw new InvalidOperationException($"Service '{typeof(TService)}' not registered.");
            }

            var stubBuilder = new NetGrpcServiceStubBuilder<TService>(
                serviceInfo, 
                this.serviceProvider.GetService<IOptions<RpcServiceOptions<TService>>>()?.Value);
            stubBuilder.Bind(this.rpcServer, typedContext);
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
}
