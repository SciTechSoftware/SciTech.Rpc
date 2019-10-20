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

using System;

namespace SciTech.Rpc.Server
{
    public static class RpcServiceDefinitionsBuilderExtensions
    {
        public static IRpcServiceDefinitionsBuilder RegisterService<TService>(this IRpcServiceDefinitionsBuilder builder, IRpcServerOptions? options = null)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));

            return builder.RegisterService(typeof(TService), null, options);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TServiceImpl">The type of the service implementation. This type will be used when retrieving attributes for the 
        /// service, e.g. related to authorization.
        /// </typeparam>
        /// <typeparam name="TService"></typeparam>
        /// <param name="builder"></param>
        /// <param name="options"></param>
        /// <returns></returns>

        public static IRpcServiceDefinitionsBuilder RegisterService<TService, TServiceImpl>(this IRpcServiceDefinitionsBuilder builder, IRpcServerOptions? options = null)
            where TServiceImpl : TService
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));

            return builder.RegisterService(typeof(TService), typeof(TServiceImpl), options);
        }

        public static IRpcServiceDefinitionsBuilder RegisterService(this IRpcServiceDefinitionsBuilder builder, Type serviceType, IRpcServerOptions? options = null)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));

            return builder.RegisterService(serviceType, null, options);
        }

        public static IRpcServiceDefinitionsBuilder RegisterImplementation<TServiceImpl>(this IRpcServiceDefinitionsBuilder builder, IRpcServerOptions? options = null)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));

            return builder.RegisterImplementation(typeof(TServiceImpl), options);
        }
    }
}
