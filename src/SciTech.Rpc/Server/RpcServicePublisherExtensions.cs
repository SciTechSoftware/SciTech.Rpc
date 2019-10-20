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

using Microsoft.Extensions.DependencyInjection;
using SciTech.Rpc.Server.Internal;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciTech.Rpc.Server
{
    public static class RpcServicePublisherExtensions
    {
        public static IList<RpcObjectRef<TService>?> GetPublishedServiceInstances<TService>(this IRpcServicePublisher servicePublisher,
            IReadOnlyList<TService> serviceInstances, bool allowUnpublished) where TService : class
        {
            if (servicePublisher is null) throw new ArgumentNullException(nameof(servicePublisher));

            if (serviceInstances == null)
            {
                return null!;
            }

            var publishedServices = new List<RpcObjectRef<TService>?>();
            foreach (var s in serviceInstances)
            {
                if (s != null)
                {
                    var publishedService = s != null ? servicePublisher.GetPublishedInstance(s) : null;
                    if (publishedService != null)
                    {
                        publishedServices.Add(publishedService);
                    }
                    else if (!allowUnpublished)
                    {
                        throw new InvalidOperationException("Service has not been published.");
                    }
                }
                else
                {
                    publishedServices.Add(null);
                }
            }

            return publishedServices;
        }

        public static ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TService, TServiceImpl>(this IRpcServicePublisher publisher)
            where TServiceImpl : class, TService
            where TService : class
        {
            if (publisher is null) throw new ArgumentNullException(nameof(publisher));

            return publisher.PublishSingleton(ServiceActivator<TService, TServiceImpl>.CreateActivatedService);
        }

        /// <summary>
        /// Publishes an RPC singleton under the service name of the <typeparamref name="TService"/> RPC interface.
        /// The service instance will be created using the <see cref="IServiceProvider"/> associated with the RPC call.
        /// </summary>
        /// <typeparam name="TService">The interface of the service type. Must be an interface type with the <see cref="RpcServiceAttribute"/> (or <c>ServiceContractAttribute)</c>) 
        /// applied.</typeparam>
        /// <returns>A scoped object including the <see cref="RpcSingletonRef{TService}"/> identifying the published singleton. The scoped object will unpublish 
        /// the service singleton when disposed.</returns>
        public static ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TService>(this IRpcServicePublisher publisher)
            where TService : class
            =>PublishSingleton<TService, TService>(publisher);


        private sealed class ServiceActivator<TService, TServiceImpl>
            where TServiceImpl : class, TService
            where TService : class
        {

            private static readonly Lazy<ObjectFactory> Factory = new Lazy<ObjectFactory>(() => ActivatorUtilities.CreateFactory(typeof(TServiceImpl), Type.EmptyTypes));

            internal static ActivatedService<TService> CreateActivatedService(IServiceProvider? services)
            {
                if (services == null)
                {
                    throw new RpcDefinitionException("An IServiceProvider must be supplied when services are published using IServiceProvider factories.");
                }

                TService service = services.GetService<TServiceImpl>();
                if (service != null)
                {
                    return new ActivatedService<TService>(service, false);
                }

                service = (TService)Factory.Value(services, Array.Empty<object>());
                return new ActivatedService<TService>(service, true);
            }
        }

    }
}
