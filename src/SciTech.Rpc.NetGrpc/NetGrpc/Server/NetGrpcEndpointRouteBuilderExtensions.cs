#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SciTech.Rpc.NetGrpc.Server.Internal;
using System;
using System.Collections.Immutable;
using System.Reflection;

namespace SciTech.Rpc.NetGrpc.Server
{
    internal class CompositeEndpointConventionBuilder : IEndpointConventionBuilder
    {
        private readonly ImmutableArray<IEndpointConventionBuilder> endpointConventionBuilders;

        public CompositeEndpointConventionBuilder(ImmutableArray<IEndpointConventionBuilder> endpointConventionBuilders)
        {
            this.endpointConventionBuilders = endpointConventionBuilders;
        }

        public void Add(Action<EndpointBuilder> convention)
        {
            foreach (var endpointConventionBuilder in this.endpointConventionBuilders)
            {
                endpointConventionBuilder.Add(convention);
            }
        }
    }

    /// <summary>
    /// Provides extension methods for <see cref="IEndpointRouteBuilder"/> to add SciTech.RPC gRPC service endpoints.
    /// </summary>
    public static class NetGrpcEndpointRouteBuilderExtensions
    {
        private static readonly MethodInfo MapGrpcServiceMethod =
            typeof(GrpcEndpointRouteBuilderExtensions).GetMethod(
                nameof(GrpcEndpointRouteBuilderExtensions.MapGrpcService),
                BindingFlags.Static | BindingFlags.Public)
                ?? throw new NotImplementedException(
                $"{nameof(GrpcEndpointRouteBuilderExtensions.MapGrpcService)} not implemented as expected on {nameof(GrpcEndpointRouteBuilderExtensions)}");

        /// <summary>
        /// Maps incoming requests to registered SciTech.Rpc gRPC services.
        /// </summary>
        /// <param name="builder">The <see cref="IEndpointRouteBuilder"/> to add the route to.</param>
        /// <returns>An <see cref="IEndpointConventionBuilder"/> for endpoints associated with the service.</returns>        
        public static IEndpointConventionBuilder MapNetGrpcServices(this IEndpointRouteBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var rpcServer = builder.ServiceProvider.GetService<NetGrpcServer>();
            if (rpcServer == null)
            {
                throw new InvalidOperationException("Unable to find the required NetGrpc services. Please add all the required NetGrpc services by calling " +
                    "'IServiceCollection.AddNetGrpc' inside the call to 'ConfigureServices(...)' in the application startup code.");

            }
            var definitionsProvider = rpcServer.ServiceDefinitionsProvider;

            var conventionBuilders = ImmutableArray.CreateBuilder<IEndpointConventionBuilder>();

            // Don't allow any more services to be registered, as that will (currently) not be be mapped
            // to HTTP/2 routes.
            definitionsProvider.Freeze();

            builder.MapGrpcService<NetGrpcServer>();
            foreach (var serviceType in definitionsProvider.GetRegisteredServiceTypes())
            {
                MapRegisteredType(builder, serviceType, conventionBuilders);
            }

            return new CompositeEndpointConventionBuilder(conventionBuilders.ToImmutable());
        }

        private static void MapRegisteredType(IEndpointRouteBuilder builder, Type serviceType, ImmutableArray<IEndpointConventionBuilder>.Builder conventionBuilders)
        {
            var activatorType = typeof(NetGrpcServiceActivator<>).MakeGenericType(serviceType);
            var typedMapGrpcServiceMethod = MapGrpcServiceMethod.MakeGenericMethod(activatorType);

            var conventionBuilder = (IEndpointConventionBuilder)typedMapGrpcServiceMethod.Invoke(null, new object[] { builder })!;
            conventionBuilders.Add(conventionBuilder);
        }
    }
}
