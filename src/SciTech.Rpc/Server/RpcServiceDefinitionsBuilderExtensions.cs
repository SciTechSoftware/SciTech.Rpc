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
        public static IRpcServiceDefinitionsBuilder RegisterService<TService>(this IRpcServiceDefinitionsBuilder builder, RpcServerOptions? options = null)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));

            return builder.RegisterService(typeof(TService), null, options);
        }

        public static IRpcServiceDefinitionsBuilder RegisterService<TServiceImpl,TService>(this IRpcServiceDefinitionsBuilder builder, RpcServerOptions? options = null)
            where TServiceImpl : TService
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));

            return builder.RegisterService(typeof(TService), typeof(TServiceImpl), options);
        }

        public static IRpcServiceDefinitionsBuilder RegisterService(this IRpcServiceDefinitionsBuilder builder, Type serviceType, RpcServerOptions? options = null)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));

            return builder.RegisterService(serviceType, null, options);
        }

        public static IRpcServiceDefinitionsBuilder RegisterImplementation<TServiceImpl>(this IRpcServiceDefinitionsBuilder builder, RpcServerOptions? options = null)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));

            return builder.RegisterImplementation(typeof(TServiceImpl), options);
        }

    }
}
