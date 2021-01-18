#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using Microsoft.Extensions.DependencyInjection;
using SciTech.Rpc.Internal;
using System;

namespace SciTech.Rpc.Server
{
    public static class RpcServerBuilderExtensions
    {
        /// <summary>
        /// Adds service specific options to an <see cref="IRpcServerBuilder"/>.
        /// </summary>
        /// <typeparam name="TService">The service type to configure.</typeparam>
        /// <param name="rpcBuilder">The <see cref="IRpcServerBuilder"/>.</param>
        /// <param name="configure">A callback to configure the service options.</param>
        /// <returns>The same instance of the <see cref="IRpcServerBuilder"/> for chaining.</returns>
        public static IRpcServerBuilder AddServiceOptions<TService>(this IRpcServerBuilder rpcBuilder,
            Action<RpcServiceOptions<TService>> configure) where TService : class
        {
            if (rpcBuilder == null)
            {
                throw new ArgumentNullException(nameof(rpcBuilder));
            }

            // GetServiceInfoFromType will throw if TService is not an RPC service (replace with an IsRpcService method)
            RpcBuilderUtil.GetServiceInfoFromType(typeof(TService));

            rpcBuilder.Services.Configure(configure);

            Rpc.ServiceCollectionExtensions.NotifyServiceRegistered<TService>(rpcBuilder.Services);
            return rpcBuilder;
        }
    }
}
