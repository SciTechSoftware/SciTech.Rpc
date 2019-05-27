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
using Microsoft.AspNetCore.Routing;
using SciTech.Rpc.NetGrpc.Server.Internal;
using System;

namespace SciTech.Rpc.NetGrpc.Server
{
    /// <summary>
    /// Provides extension methods for <see cref="IEndpointRouteBuilder"/> to add SciTech.RPC gRPC service endpoints.
    /// </summary>
    public static class NetGrpcEndpointRouteBuilderExtensions
    {
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

            return builder.MapGrpcService<NetGrpcServiceActivator>();
        }
    }
}
