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
using SciTech.ComponentModel;
using SciTech.Rpc.Server.Internal;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciTech.Rpc.Server
{
    public static class RpcServicePublisherExtensions
    {
        /// <summary>
        /// Publishes an RPC service instance with the help of a service provider factory.
        /// </summary>
        /// <typeparam name="TService">The type of the published instance.</typeparam>
        /// <param name="factory">A factory function that should create the service instance specified by the <see cref="RpcObjectId"/>
        /// with the help of the provided <see cref="IServiceProvider"/>.</param>
        /// <returns>A scoped object including the <see cref="RpcObjectRef"/> identifying the published instance. The scoped object will unpublish 
        /// the service instance when disposed.</returns>
        public static IOwned<RpcObjectRef<TService>> PublishInstance<TService>(this IRpcServicePublisher servicePublisher, Func<IServiceProvider, RpcObjectId, TService> factory) 
            where TService : class
        {
            if (servicePublisher is null) throw new ArgumentNullException(nameof(servicePublisher));

            ActivatedService<TService> CreateActivatedService(IServiceProvider? services, RpcObjectId objectId)
            {
                if (services == null)
                {
                    throw new RpcDefinitionException("An IServiceProvider must be supplied when services are published using IServiceProvider factories.");
                }

                return new ActivatedService<TService>(factory(services, objectId), false);
            }

            return servicePublisher.PublishInstance(CreateActivatedService);
        }

        /// <summary>
        /// Publishes an RPC service instance using an instance factory.
        /// </summary>
        /// <typeparam name="TService">The type of the published instance.</typeparam>
        /// <param name="factory">A factory function that should create the service instance specified by the <see cref="RpcObjectId"/>. If the created
        /// instance implements <see cref="IDisposable"/> the instance will be disposed when the RPC call has finished.
        /// </param>    
        /// <returns>A scoped object including the <see cref="RpcObjectRef"/> identifying the published instance. The scoped object will unpublish 
        /// the service instance when disposed.</returns>
        public static IOwned<RpcObjectRef<TService>> PublishInstance<TService>(this IRpcServicePublisher servicePublisher, Func<RpcObjectId, TService> factory) 
            where TService : class
        {
            if (servicePublisher is null) throw new ArgumentNullException(nameof(servicePublisher));

            if (servicePublisher is null) throw new ArgumentNullException(nameof(servicePublisher));

            ActivatedService<TService> CreateActivatedService(IServiceProvider? services, RpcObjectId objectId)
            {
                return new ActivatedService<TService>(factory(objectId), true);
            }

            return servicePublisher.PublishInstance(CreateActivatedService);
        }


        public static IOwned<RpcSingletonRef<TService>> PublishSingleton<TService>(this IRpcServicePublisher servicePublisher, Func<IServiceProvider, TService> factory)
            where TService : class
        {
            if (servicePublisher is null) throw new ArgumentNullException(nameof(servicePublisher));

            ActivatedService<TService> CreateActivatedService(IServiceProvider? services)
            {
                if (services == null)
                {
                    throw new RpcDefinitionException("An IServiceProvider must be supplied when services are published using IServiceProvider factories.");
                }

                return new ActivatedService<TService>(factory(services), false);
            }

            return servicePublisher.PublishSingleton(CreateActivatedService);
        }

        public static IOwned<RpcSingletonRef<TService>> PublishSingleton<TService>(this IRpcServicePublisher servicePublisher, Func<TService> factory)
            where TService : class
        {
            if (servicePublisher is null) throw new ArgumentNullException(nameof(servicePublisher));

            ActivatedService<TService> CreateActivatedService(IServiceProvider? _) => new ActivatedService<TService>(factory(), true);

            return servicePublisher.PublishSingleton(CreateActivatedService);
        }

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

        /// <summary>
        /// Publishes an RPC singleton under the service name of the <typeparamref name="TService"/> RPC interface,
        /// using <typeparamref name="TServiceImpl"/> as the implementation.
        /// The service instance will be created using the <see cref="IServiceProvider"/> associated with the RPC call.
        /// </summary>
        /// <typeparam name="TService">The interface of the service type. Must be an interface type with the <see cref="RpcServiceAttribute"/> (or <c>ServiceContractAttribute</c>) 
        /// applied.</typeparam>
        /// <typeparam name="TServiceImpl">The type that implements the service interface.</typeparam>
        /// <returns>A scoped object including the <see cref="RpcSingletonRef{TService}"/> identifying the published singleton. The scoped object will unpublish 
        /// the service singleton when disposed.</returns>
        public static IOwned<RpcSingletonRef<TService>> PublishSingleton<TService, TServiceImpl>(this IRpcServicePublisher publisher)
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
        /// <typeparam name="TService">The interface of the service type. Must be an interface type with the <see cref="RpcServiceAttribute"/> (or <c>ServiceContractAttribute</c>) 
        /// applied.</typeparam>
        /// <returns>A scoped object including the <see cref="RpcSingletonRef{TService}"/> identifying the published singleton. The scoped object will unpublish 
        /// the service singleton when disposed.</returns>
        public static IOwned<RpcSingletonRef<TService>> PublishSingleton<TService>(this IRpcServicePublisher publisher)
            where TService : class
            => PublishSingleton<TService, TService>(publisher);


        private sealed class ServiceActivator<TService, TServiceImpl>
            where TServiceImpl : class, TService
            where TService : class
        {

            private static readonly Lazy<ObjectFactory> Factory = new Lazy<ObjectFactory>(() => ActivatorUtilities.CreateFactory(typeof(TServiceImpl), Type.EmptyTypes));

            internal static ActivatedService<TService> CreateActivatedService(IServiceProvider? services)
            {
                if (services == null)
                {
                    throw new RpcFailureException(RpcFailure.RemoteDefinitionError, "An IServiceProvider must be supplied when services are published using IServiceProvider factories.");
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
