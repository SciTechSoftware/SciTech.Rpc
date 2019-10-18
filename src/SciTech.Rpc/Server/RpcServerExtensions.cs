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
using System.Linq;
using SciTech.Rpc.Server;

namespace SciTech.Rpc.Server
{
    public static class RpcServerExtensions
    {
        public static ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TServiceImpl, TService>(this IRpcServer server) where TService : class where TServiceImpl : class, TService
        {
            if (server is null) throw new ArgumentNullException(nameof(server));

            return server.ServicePublisher.PublishSingleton<TServiceImpl, TService>();
        }

        public static RpcObjectRef<TService>? GetPublishedServiceInstance<TService>(this IRpcServer server, TService serviceInstance) where TService : class
        {
            if (server is null) throw new ArgumentNullException(nameof(server));

            return server.ServicePublisher.GetPublishedInstance(serviceInstance);
        }

        public static ScopedObject<RpcObjectRef<TService>> PublishServiceInstance<TService>(this IRpcServer server, TService serviceInstance, bool takeOwnership = false) where TService : class
        {
            if (server is null) throw new ArgumentNullException(nameof(server));
            return server.ServicePublisher.PublishInstance(serviceInstance, takeOwnership);
        }

        public static ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TService>(this IRpcServer server, Func<TService> singletonFactory) where TService : class
        {
            if (server is null) throw new ArgumentNullException(nameof(server));

            return server.ServicePublisher.PublishSingleton<TService>(singletonFactory);
        }

        public static ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TService>(this IRpcServer server, Func<IServiceProvider, TService> singletonFactory) where TService : class
        {
            if (server is null) throw new ArgumentNullException(nameof(server));

            return server.ServicePublisher.PublishSingleton(singletonFactory);
        }

        public static ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TService>(this IRpcServer server, TService singletonService, bool takeOwnership = false) where TService : class
        {
            if (server is null) throw new ArgumentNullException(nameof(server));

            return server.ServicePublisher.PublishSingleton(singletonService, takeOwnership);
        }



        public static void UnpublishInstance(this IRpcServer server, RpcObjectId serviceInstanceId)
        {
            if (server is null) throw new ArgumentNullException(nameof(server));

            server.ServicePublisher.UnpublishInstance(serviceInstanceId);
        }

        public static void UnpublishSingleton<TService>(this IRpcServer server) where TService : class
        {
            if (server is null) throw new ArgumentNullException(nameof(server));

            server.ServicePublisher.UnpublishSingleton<TService>();
        }
    }
}
