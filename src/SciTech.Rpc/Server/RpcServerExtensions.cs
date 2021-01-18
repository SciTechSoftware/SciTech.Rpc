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

using SciTech.ComponentModel;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace SciTech.Rpc.Server
{
    public static class RpcServerExtensions
    {
        public static IOwned<RpcSingletonRef<TService>> PublishSingleton<TService, TServiceImpl>(this IRpcServer server) where TService : class where TServiceImpl : class, TService
        {
            if (server is null) throw new ArgumentNullException(nameof(server));

            return server.ServicePublisher.PublishSingleton<TService, TServiceImpl>();
        }
        public static IOwned<RpcSingletonRef<TService>> PublishSingleton<TService>(this IRpcServer server) where TService : class
        {
            if (server is null) throw new ArgumentNullException(nameof(server));

            return server.ServicePublisher.PublishSingleton<TService>();
        }

        public static RpcObjectRef<TService>? GetPublishedServiceInstance<TService>(this IRpcServer server, TService serviceInstance) where TService : class
        {
            if (server is null) throw new ArgumentNullException(nameof(server));

            return server.ServicePublisher.GetPublishedInstance(serviceInstance);
        }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership transferred")]
        public static IOwned<RpcObjectRef<TService>> PublishInstance<TService>(this IRpcServer server, TService serviceInstance, bool takeOwnership = false) where TService : class
        {
            if (server is null) throw new ArgumentNullException(nameof(server));
            return server.ServicePublisher.PublishInstance(takeOwnership ? OwnedObject.Create(serviceInstance) : OwnedObject.CreateUnowned(serviceInstance));
        }

        public static IOwned<RpcObjectRef<TService>> PublishInstance<TService>(this IRpcServer server, IOwned<TService> serviceInstance) where TService : class
        {
            if (server is null) throw new ArgumentNullException(nameof(server));
            return server.ServicePublisher.PublishInstance(serviceInstance);
        }

        public static IOwned<RpcSingletonRef<TService>> PublishSingleton<TService>(this IRpcServer server, Func<TService> singletonFactory) where TService : class
        {
            if (server is null) throw new ArgumentNullException(nameof(server));

            return server.ServicePublisher.PublishSingleton<TService>(singletonFactory);
        }

        public static IOwned<RpcSingletonRef<TService>> PublishSingleton<TService>(this IRpcServer server, Func<IServiceProvider, TService> singletonFactory) where TService : class
        {
            if (server is null) throw new ArgumentNullException(nameof(server));

            return server.ServicePublisher.PublishSingleton<TService>(singletonFactory);
        }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership transferred")]
        public static IOwned<RpcSingletonRef<TService>> PublishSingleton<TService>(this IRpcServer server, TService singletonService, bool takeOwnership = false) where TService : class
        {
            if (server is null) throw new ArgumentNullException(nameof(server));

            return server.ServicePublisher.PublishSingleton(takeOwnership ? OwnedObject.Create(singletonService) : OwnedObject.CreateUnowned(singletonService));
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
