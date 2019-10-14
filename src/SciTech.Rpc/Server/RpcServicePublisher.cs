﻿#region Copyright notice and license
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
using SciTech.Rpc.Internal;
using SciTech.Rpc.Server.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace SciTech.Rpc.Server
{
    public enum RpcInstanceLifetime
    {
        InstancePerCall,
        InstancePerSession,
        Singleton
    }

    [Flags]
    public enum RpcInstanceOptions
    {
        Normal = 0x0000,
        Weak = 0x0001
    }

    /// <summary>
    /// Provides functionality to publish RPC service object instances and singleton instances. 
    /// </summary>
    public interface IRpcServicePublisher
    {

        RpcServerConnectionInfo? ConnectionInfo { get; }

        RpcServerId ServerId { get; }

        RpcObjectRef<TService> GetOrPublishInstance<TService>(TService serviceInstance) where TService : class;

        RpcObjectRef<TService>? GetPublishedInstance<TService>(TService serviceInstance) where TService : class;

        void InitConnectionInfo(RpcServerConnectionInfo connectionInfo);

        /// <summary>
        /// Publishes an RPC service instance with the help of a service provider factory.
        /// </summary>
        /// <typeparam name="TService">The type of the published instance.</typeparam>
        /// <param name="factory">A factory function that should create the service instance specified by the <see cref="RpcObjectId"/>
        /// with the help of the provided <see cref="IServiceProvider"/>.</param>
        /// <returns>A scoped object including the <see cref="RpcObjectRef"/> identifying the published instance. The scoped object will unpublish 
        /// the service instance when disposed.</returns>
        ScopedObject<RpcObjectRef<TService>> PublishInstance<TService>(Func<IServiceProvider, RpcObjectId, TService> factory) where TService : class;

        /// <summary>
        /// Publishes an RPC service instance using an instance factory.
        /// </summary>
        /// <typeparam name="TService">The type of the published instance.</typeparam>
        /// <param name="factory">A factory function that should create the service instance specified by the <see cref="RpcObjectId"/>. If the created
        /// instance implements <see cref="IDisposable"/> the instance will be disposed when the RPC call has finished.
        /// </param>    
        /// <returns>A scoped object including the <see cref="RpcObjectRef"/> identifying the published instance. The scoped object will unpublish 
        /// the service instance when disposed.</returns>
        ScopedObject<RpcObjectRef<TService>> PublishInstance<TService>(Func<RpcObjectId, TService> factory) where TService : class;

        /// <summary>
        /// Publishes an RPC service instance.
        /// </summary>
        /// <typeparam name="TService"></typeparam>
        /// <param name="serviceInstance">The </param>
        /// <param name="takeOwnership"><c>true</c> to indicate that the instance should be disposed when unpublished.</param>
        /// <returns>A scoped object including the <see cref="RpcObjectRef"/> identifying the published instance. The scoped object will unpublish 
        /// the service instance when disposed.</returns>
        ScopedObject<RpcObjectRef<TService>> PublishInstance<TService>(TService serviceInstance, bool takeOwnership = false) where TService : class;

        /// <summary>
        /// Publishes an RPC singleton under the service name of the <typeparamref name="TService"/> RPC interface.
        /// The service instance will be created using the <see cref="IServiceProvider"/> associated with the RPC call.
        /// </summary>
        /// <typeparam name="TServiceImpl">The type of the service implementation. This type will be used when resolving the service implementation using 
        /// the <see cref="IServiceProvider"/> associated with the RPC call.
        /// </typeparam>
        /// <typeparam name="TService">The interface of the service type. Must be an interface type with the <see cref="RpcServiceAttribute"/> (or <c>ServiceContractAttribute)</c>)
        /// applied.</typeparam>
        /// <returns>A scoped object including the <see cref="RpcSingletonRef{TService}"/> identifying the published singleton. The scoped object will unpublish 
        /// the service singleton when disposed.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TServiceImpl, TService>()
            where TService : class
            where TServiceImpl : class, TService;

        ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TService>(Func<IServiceProvider, TService> factory)
            where TService : class;

        ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TService>(Func<TService> factory)
            where TService : class;

        ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TService>(TService singletonService, bool takeOwnership = false) where TService : class;

        /// <summary>
        /// Gets the connection info associated with this service publisher. If the connection
        /// info has not been initialized, this method will initialize the connection info
        /// and then return <see cref="ConnectionInfo"/>.
        /// </summary>
        /// <returns>The initialized <see cref="ConnectionInfo"/></returns>
        RpcServerConnectionInfo RetrieveConnectionInfo();

        /// <summary>
        /// Gets the server identifier associated with this service publisher. If the server
        /// identifier has not been initialized, a new identifier will be assigned to <see cref="ServerId"/>
        /// and returned.
        /// </summary>
        /// <returns>The initialized <see cref="ServerId"/></returns>
        RpcServerId RetrieveServerId();

        RpcServerConnectionInfo TryInitConnectionInfo(RpcServerConnectionInfo connectionInfo);

        void UnpublishInstance(RpcObjectId serviceInstanceId);

        void UnpublishSingleton<TService>() where TService : class;

    }

    /// <summary>
    /// Default implementation of <see cref="IRpcServicePublisher"/>. 
    /// </summary>
    public sealed class RpcServicePublisher : IRpcServicePublisher, IRpcServiceActivator
    {

        private readonly Dictionary<RpcObjectId, PublishedServices> idToPublishedServices = new Dictionary<RpcObjectId, PublishedServices>();

        /// <summary>
        /// Value is <see cref="Func{IServiceProvider,RpcObjectId,TService}"/> or <see cref="Func{RpcObjectId,TService}"/>.
        /// </summary>
        private readonly Dictionary<ServiceImplKey, Delegate> idToServiceFactory
            = new Dictionary<ServiceImplKey, Delegate>();

        private readonly Dictionary<ServiceImplKey, PublishedInstance> idToServiceImpl = new Dictionary<ServiceImplKey, PublishedInstance>();

        private readonly Dictionary<InstanceKey, RpcObjectId> serviceImplToId = new Dictionary<InstanceKey, RpcObjectId>();

        /// <summary>
        /// Value is <see cref="Func{IServiceProvider,TService}"/> or <see cref="Func{TService}"/>.
        /// </summary>
        private readonly Dictionary<Type, Delegate> singletonTypeToFactory = new Dictionary<Type, Delegate>();

        /// <summary>
        /// Maps a published singleton type to all RPC interface types published under that singleton.
        /// </summary>
        private readonly Dictionary<Type, PublishedServices> singletonTypeToPublishedServices = new Dictionary<Type, PublishedServices>();

        /// <summary>
        /// Value is <see cref="Func{IServiceProvider,TService}"/> or <see cref="Func{TService}"/>.
        /// </summary>
        private readonly Dictionary<Type, PublishedInstance> singletonTypeToServiceImpl = new Dictionary<Type, PublishedInstance>();

        private readonly object syncRoot = new object();

        private RpcServerConnectionInfo? connectionInfo;

        private bool connectionInfoRetrieved;

        private RpcServerId serverId;

        public RpcServicePublisher(IRpcServiceDefinitionsProvider serviceDefinitionsProvider, RpcServerId serverId = default)
        {
            this.DefinitionsProvider = serviceDefinitionsProvider ?? throw new ArgumentNullException(nameof(serviceDefinitionsProvider));
            this.serverId = serverId;
        }


        public RpcServicePublisher(RpcServerConnectionInfo connectionInfo, IRpcServiceDefinitionsProvider serviceDefinitionsProvider)
        {
            this.InitConnectionInfo(connectionInfo);
            this.DefinitionsProvider = serviceDefinitionsProvider ?? throw new ArgumentNullException(nameof(serviceDefinitionsProvider));
        }

        public RpcServerConnectionInfo? ConnectionInfo
        {
            get
            {
                lock (this.syncRoot)
                {
                    //if (this.connectionInfo == null)
                    //{
                    //    return null;
                    //}

                    //this.connectionInfoRetrieved = true;
                    return this.connectionInfo;
                }
            }
        }

        public IRpcServiceDefinitionsProvider DefinitionsProvider { get; }

        public RpcServerId ServerId
        {
            get
            {
                lock (this.syncRoot)
                {
                    return this.serverId;
                }
            }
        }

        public RpcObjectRef<TService> GetOrPublishInstance<TService>(TService serviceInstance) where TService : class
        {
            if (serviceInstance is null) throw new ArgumentNullException(nameof(serviceInstance));

            InstanceKey key;
            lock (this.syncRoot)
            {
                key = new InstanceKey(serviceInstance, false);
                if (this.serviceImplToId.TryGetValue(key, out var instanceId))
                {
                    return new RpcObjectRef<TService>(this.connectionInfo, instanceId, this.GetPublishedServices(instanceId).ToArray());
                }
            }

            // Not published, so we try to register the serviceInstance's service definitions 
            // and then publish it.

            var allServices = RpcBuilderUtil.GetAllServices(serviceInstance.GetType(), true);
            this.TryRegisterServiceDefinitions(allServices, serviceInstance.GetType());

            var connectionInfo = this.RetrieveConnectionInfo();

            lock (this.syncRoot)
            {
                // Let's try again.
                if (this.serviceImplToId.TryGetValue(key, out var instanceId))
                {
                    // Somebody beat us to it.
                    return new RpcObjectRef<TService>(this.connectionInfo, instanceId, this.GetPublishedServices(instanceId).ToArray());
                }

                var objectId = RpcObjectId.NewId();
                var newPublishedServices = this.PublishInstanceCore_Locked(allServices, serviceInstance, objectId, true, false);
                return new RpcObjectRef<TService>(connectionInfo, objectId, newPublishedServices.ToArray());
            }
        }

        public RpcObjectRef<TService>? GetPublishedInstance<TService>(TService serviceInstance) where TService : class
        {
            var connectionInfo = this.RetrieveConnectionInfo();

            lock (this.syncRoot)
            {
                var key = new InstanceKey(serviceInstance, false);
                if (this.serviceImplToId.TryGetValue(key, out var objectId))
                {
                    return new RpcObjectRef<TService>(connectionInfo, objectId, this.GetPublishedServices(objectId).ToArray());
                }
            }

            return null;
        }

        public ImmutableArray<string> GetPublishedServices(RpcObjectId objectId)
        {
            lock (this.syncRoot)
            {
                if (this.idToPublishedServices.TryGetValue(objectId, out var servicesList))
                {
                    return servicesList.ServiceNames;
                }

                return ImmutableArray<string>.Empty;
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="value"></param>
        public void InitConnectionInfo(RpcServerConnectionInfo value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));

            lock (this.syncRoot)
            {
                if (!Equals(this.connectionInfo, value))
                {
                    if (this.connectionInfoRetrieved)
                    {
                        throw new InvalidOperationException("Cannot change ConnectionInfo after it has been retrieved.");
                    }

                    if (this.serverId != RpcServerId.Empty)
                    {
                        if (value.ServerId == RpcServerId.Empty)
                        {
                            this.connectionInfo = value.SetServerId(this.serverId);
                        }
                        else if (this.serverId != value.ServerId)
                        {
                            throw new InvalidOperationException("Cannot change server id after it has been assigned.");
                        }
                    }
                    else
                    {
                        this.connectionInfo = value;
                        this.serverId = value.ServerId;
                    }
                }
            }
        }

        public ScopedObject<RpcObjectRef<TService>> PublishInstance<TService>(Func<IServiceProvider, RpcObjectId, TService> factory) where TService : class
        {
            var allServices = RpcBuilderUtil.GetAllServices(typeof(TService), RpcServiceDefinitionSide.Server, true);
            this.TryRegisterServiceDefinitions(allServices, null);

            var connectionInfo = this.RetrieveConnectionInfo();

            ActivatedService<TService> CreateActivatedService(IServiceProvider? services, RpcObjectId objectId)
            {
                if (services == null)
                {
                    throw new RpcDefinitionException("An IServiceProvider must be supplied when services are published using IServiceProvider factories.");
                }

                return new ActivatedService<TService>(factory(services, objectId), false);
            }

            lock (this.syncRoot)
            {
                var objectId = RpcObjectId.NewId();

                var publishedServices = this.PublishInstanceFactoryCore_Locked(allServices, objectId, CreateActivatedService);

                return new ScopedObject<RpcObjectRef<TService>>(new RpcObjectRef<TService>(
                    connectionInfo, objectId, publishedServices.ToArray()), () => this.UnpublishInstance(objectId));

            }
        }

        public ScopedObject<RpcObjectRef<TService>> PublishInstance<TService>(Func<RpcObjectId, TService> factory) where TService : class
        {
            var allServices = RpcBuilderUtil.GetAllServices(typeof(TService), RpcServiceDefinitionSide.Server, true);
            this.TryRegisterServiceDefinitions(allServices, null);

            var connectionInfo = this.RetrieveConnectionInfo();

            ActivatedService<TService> CreateActivatedService(IServiceProvider? services, RpcObjectId objectId)
            {
                return new ActivatedService<TService>(factory(objectId), true);
            }

            lock (this.syncRoot)
            {
                RpcObjectId objectId = RpcObjectId.NewId();

                var publishedServices = this.PublishInstanceFactoryCore_Locked(allServices, objectId, CreateActivatedService);

                return new ScopedObject<RpcObjectRef<TService>>(new RpcObjectRef<TService>(
                    connectionInfo, objectId, publishedServices.ToArray()), () => this.UnpublishInstance(objectId));

            }
        }

        public ScopedObject<RpcObjectRef<TService>> PublishInstance<TService>(TService serviceInstance, bool takeOwnership = false)
            where TService : class
        {
            if (serviceInstance is null) throw new ArgumentNullException(nameof(serviceInstance));

            var connectionInfo = this.RetrieveConnectionInfo();
            lock (this.syncRoot)
            {
                var serviceInstanceId = RpcObjectId.NewId();

                var allServices = RpcBuilderUtil.GetAllServices(serviceInstance.GetType(), true);
                var publishedServices = this.PublishInstanceCore_Locked(allServices, serviceInstance, serviceInstanceId, false, takeOwnership);

                Action disposeAction;
                if (takeOwnership && serviceInstance is IDisposable disposableService)
                {
                    disposeAction = () =>
                    {
                        this.UnpublishInstance(serviceInstanceId);
                        disposableService.Dispose();
                    };
                }
                else
                {
                    disposeAction = () => this.UnpublishInstance(serviceInstanceId);
                }

                return new ScopedObject<RpcObjectRef<TService>>(new RpcObjectRef<TService>(
                    connectionInfo, serviceInstanceId, publishedServices.ToArray()), disposeAction);
            }
        }

        public ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TServiceImpl, TService>()
            where TServiceImpl : class, TService
            where TService : class
        {
            this.PublishSingletonFactoryCore<TServiceImpl, TService>(ServiceActivator<TServiceImpl, TService>.CreateActivatedService);

            return new ScopedObject<RpcSingletonRef<TService>>(new RpcSingletonRef<TService>(
                this.RetrieveConnectionInfo()), () => this.UnpublishSingleton<TService>());
        }


        public ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TService>(Func<IServiceProvider, TService> factory)
            where TService : class
        {
            ActivatedService<TService> CreateActivatedService(IServiceProvider? services)
            {
                if (services == null)
                {
                    throw new RpcDefinitionException("An IServiceProvider must be supplied when services are published using IServiceProvider factories.");
                }

                return new ActivatedService<TService>(factory(services), false);
            }

            this.PublishSingletonFactoryCore<TService, TService>(CreateActivatedService);

            return new ScopedObject<RpcSingletonRef<TService>>(new RpcSingletonRef<TService>(
                this.RetrieveConnectionInfo()), () => this.UnpublishSingleton<TService>());
        }

        public ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TService>(Func<TService> factory)
            where TService : class
        {
            ActivatedService<TService> CreateActivatedService(IServiceProvider? _) => new ActivatedService<TService>(factory(), true);

            this.PublishSingletonFactoryCore<TService, TService>(CreateActivatedService);

            return new ScopedObject<RpcSingletonRef<TService>>(new RpcSingletonRef<TService>(
                this.RetrieveConnectionInfo()), () => this.UnpublishSingleton<TService>());
        }

        public ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TService>(TService singletonService, bool takeOwnership = false) where TService : class
        {
            if (singletonService == null) throw new ArgumentNullException(nameof(singletonService));

            var allServices = RpcBuilderUtil.GetAllServices(typeof(TService), false);

            this.TryRegisterServiceDefinitions(allServices, singletonService.GetType());
            var publishedServices = this.VerifyPublishedServices(allServices);

            var connectionInfo = this.RetrieveConnectionInfo();
            lock (this.syncRoot)
            {
                var instanceKey = new InstanceKey(singletonService, false);

                foreach (var serviceType in publishedServices.ServiceTypes)
                {
                    if (this.singletonTypeToServiceImpl.ContainsKey(serviceType) || this.singletonTypeToFactory.ContainsKey(serviceType))
                    {
                        throw new RpcDefinitionException($"A singleton for the type '{serviceType}' has already been published.");
                    }
                }

                this.singletonTypeToPublishedServices.Add(typeof(TService), publishedServices);
                foreach (var serviceType in publishedServices.ServiceTypes)
                {
                    this.singletonTypeToServiceImpl.Add(serviceType, instanceKey.GetPublishedInstance(takeOwnership));
                }
            }

            return new ScopedObject<RpcSingletonRef<TService>>(new RpcSingletonRef<TService>(
                connectionInfo), () => this.UnpublishSingleton<TService>());
        }

        public RpcServerConnectionInfo RetrieveConnectionInfo()
        {
            lock (this.syncRoot)
            {
                this.InitServerId();

                if (this.connectionInfo == null)
                {
                    this.connectionInfo = new RpcServerConnectionInfo("RpcServer", null, this.serverId);
                }
                else if (this.connectionInfo.ServerId != this.serverId)
                {
                    Debug.Assert(this.connectionInfo.ServerId == RpcServerId.Empty);
                    this.connectionInfo = this.connectionInfo.SetServerId(this.serverId);
                }

                this.connectionInfoRetrieved = true;
                return this.connectionInfo;
            }
        }

        public RpcServerId RetrieveServerId()
        {
            lock (this.syncRoot)
            {
                this.InitServerId();

                return this.serverId;
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="value"></param>
        public RpcServerConnectionInfo TryInitConnectionInfo(RpcServerConnectionInfo value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            lock (this.syncRoot)
            {
                if (!Equals(this.connectionInfo, value))
                {
                    if (this.connectionInfo == null)
                    {
                        this.connectionInfo = value;
                    }
                    else
                    {
                        // We already have a connection info. If it's missing a server id, let's update server id 
                        // if provided.
                        if (this.serverId == RpcServerId.Empty)
                        {
                            if (value.ServerId != RpcServerId.Empty)
                            {
                                this.connectionInfo = this.connectionInfo.SetServerId(value.ServerId);
                                this.serverId = value.ServerId;
                            }
                        }
                        else
                        {
                            if (value.ServerId != RpcServerId.Empty && value.ServerId != this.serverId)
                            {
                                throw new InvalidOperationException("Server id of provided connection does not match already assigned server id.");
                            }
                        }
                    }
                }

                return this.connectionInfo!;
            }
        }

        public void UnpublishInstance(RpcObjectId serviceInstanceId)
        {
            PublishedInstance? removedInstance = null;

            lock (this.syncRoot)
            {
                if (this.idToPublishedServices.TryGetValue(serviceInstanceId, out var publishedServices))
                {
                    foreach (var serviceType in publishedServices.ServiceTypes)
                    {
                        var serviceKey = new ServiceImplKey(serviceInstanceId, serviceType);
                        if (this.idToServiceImpl.TryGetValue(serviceKey, out var publishedInstance))
                        {
                            this.idToServiceImpl.Remove(serviceKey);
                            if (removedInstance == null)
                            {
                                removedInstance = publishedInstance;
                            }
                            else
                            {
                                Debug.Assert(Equals(removedInstance, publishedInstance));
                            }
                        }

                        this.idToServiceFactory.Remove(serviceKey);
                    }
                }

                if (removedInstance != null)
                {
                    var instance = removedInstance.Value.GetInstance();
                    if (instance != null)
                    {
                        this.serviceImplToId.Remove(new InstanceKey(instance, false));
                    }
                }
            }

            if (removedInstance?.Owned == true && removedInstance.Value.GetInstance() is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        public void UnpublishSingleton<TService>() where TService : class
        {
            PublishedInstance? removedInstance = null;

            lock (this.syncRoot)
            {
                if (this.singletonTypeToPublishedServices.TryGetValue(typeof(TService), out var publishedTypes))
                {
                    foreach (var serviceType in publishedTypes.ServiceTypes)
                    {
                        this.singletonTypeToFactory.Remove(serviceType);
                        if (this.singletonTypeToServiceImpl.TryGetValue(serviceType, out var publishedInstance))
                        {
                            this.singletonTypeToServiceImpl.Remove(serviceType);
                            if (removedInstance == null)
                            {
                                removedInstance = publishedInstance;
                            }
                            else
                            {
                                Debug.Assert(Equals(removedInstance, publishedInstance));
                            }
                        }
                    }
                }
            }

            if (removedInstance?.Owned == true && removedInstance.Value.GetInstance() is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        ActivatedService<TService>? IRpcServiceActivator.GetActivatedService<TService>(IServiceProvider? serviceProvider, RpcObjectId id) where TService : class
        {
            var key = new ServiceImplKey(id, typeof(TService));
            lock (this.syncRoot)
            {
                if (id != RpcObjectId.Empty)
                {
                    if (this.idToServiceImpl.TryGetValue(key, out var serviceImpl) && serviceImpl.GetInstance() is TService service)
                    {
                        return new ActivatedService<TService>(service, false);
                    }

                    if (this.idToServiceFactory.TryGetValue(key, out var serviceFactory))
                    {
                        return ((Func<IServiceProvider?, RpcObjectId, ActivatedService<TService>>)serviceFactory)(serviceProvider, id);
                    }
                }
                else
                {
                    if (this.singletonTypeToServiceImpl.TryGetValue(typeof(TService), out var serviceImpl) && serviceImpl.GetInstance() is TService service)
                    {
                        return new ActivatedService<TService>(service, false);
                    }

                    if (this.singletonTypeToFactory.TryGetValue(typeof(TService), out var singletonfactory))
                    {
                        return ((Func<IServiceProvider?, ActivatedService<TService>>)singletonfactory)(serviceProvider);
                    }
                }
            }

            return null;
        }

        private void InitServerId()
        {
            if (this.serverId == RpcServerId.Empty)
            {
                this.serverId = RpcServerId.NewId();
            }
        }

        private IReadOnlyCollection<string> PublishInstanceCore_Locked(
            IReadOnlyList<RpcServiceInfo> allServices,
            object serviceInstance,
            RpcObjectId serviceInstanceId,
            bool isWeak,
            bool takeOwnership)
        {
            Debug.Assert(serviceInstanceId != RpcObjectId.Empty);

            var key = new InstanceKey(serviceInstance, isWeak);
            if (this.serviceImplToId.ContainsKey(key))
            {
                throw new InvalidOperationException("Service instance already published.");
            }

            if (allServices.Count == 0)
            {
                throw new ArgumentException("The published instance does not implement any RPC service interface.", nameof(serviceInstance));
            }


            var publishedServices = this.VerifyPublishedServices(allServices);


            if (serviceInstanceId != RpcObjectId.Empty)
            {
                foreach (var serviceType in publishedServices.ServiceTypes)
                {
                    this.idToServiceImpl.Add(new ServiceImplKey(serviceInstanceId, serviceType), key.GetPublishedInstance(takeOwnership));
                }

                this.idToPublishedServices.Add(serviceInstanceId, publishedServices);
            }

            this.serviceImplToId.Add(key, serviceInstanceId);

            return publishedServices.ServiceNames;
        }

        private ImmutableArray<string> PublishInstanceFactoryCore_Locked<TService>(IReadOnlyList<RpcServiceInfo> allServices, RpcObjectId objectId, Func<IServiceProvider, RpcObjectId, ActivatedService<TService>> factory)
            where TService : class
        {
            Debug.Assert(objectId != RpcObjectId.Empty);
            var publishedServices = this.VerifyPublishedServices(allServices);

            foreach (var serviceType in publishedServices.ServiceTypes)
            {
                this.idToServiceFactory.Add(new ServiceImplKey(objectId, serviceType), factory);
            }

            return publishedServices.ServiceNames;
        }

        private ImmutableArray<string> PublishSingletonFactoryCore<TServiceImpl, TService>(Func<IServiceProvider?, ActivatedService<TService>> factory)
            where TService : class
            where TServiceImpl : class, TService
        {
            // Getting the ServiceInfo validates that TService is actually an RPC service interface.
            RpcBuilderUtil.GetServiceInfoFromType(typeof(TService));

            var allServices = RpcBuilderUtil.GetAllServices(typeof(TServiceImpl), typeof(TServiceImpl), RpcServiceDefinitionSide.Server, true);
            this.TryRegisterServiceDefinitions(allServices, typeof(TServiceImpl));

            var publishedServices = this.VerifyPublishedServices(allServices);

            lock (this.syncRoot)
            {
                foreach (var serviceType in publishedServices.ServiceTypes)
                {
                    if (this.singletonTypeToFactory.ContainsKey(serviceType) || this.singletonTypeToServiceImpl.ContainsKey(serviceType))
                    {
                        throw new RpcDefinitionException($"A singleton for the type '{serviceType}' has already been published.");
                    }
                }

                this.singletonTypeToPublishedServices.Add(typeof(TService), publishedServices);
                foreach (var serviceType in publishedServices.ServiceTypes)
                {
                    this.singletonTypeToFactory.Add(serviceType, factory);
                }
            }

            return publishedServices.ServiceNames;
        }

        private void TryRegisterServiceDefinitions(IReadOnlyList<RpcServiceInfo> allServices, Type? implementationType)
        {
            if (this.DefinitionsProvider is IRpcServiceDefinitionsBuilder builder)
            {
                if (!builder.IsFrozen)
                {
                    foreach (var service in allServices)
                    {
                        if (!builder.IsServiceRegistered(service.Type) )
                        {
                            builder.RegisterService(service.Type, implementationType);
                        }
                    }
                }
            }
        }

        private PublishedServices VerifyPublishedServices(IReadOnlyList<RpcServiceInfo> allServices)
        {
            if (allServices.Count == 0)
            {
                throw new ArgumentException("The published service type does not implement any RPC service interface.");
            }

            var serviceNamesBuilder = ImmutableArray.CreateBuilder<string>(allServices.Count);
            var serviceTypesBuilder = ImmutableArray.CreateBuilder<Type>(allServices.Count);

            foreach (var serviceInfo in allServices)
            {
                if (!this.DefinitionsProvider.IsServiceRegistered(serviceInfo.Type))
                {
                    throw new RpcDefinitionException($"Published service '{serviceInfo.Type}' is not registered.");
                }

                if (!serviceNamesBuilder.Contains(serviceInfo.FullName))
                {
                    serviceNamesBuilder.Add(serviceInfo.FullName);
                }

                serviceTypesBuilder.Add(serviceInfo.Type);
            }

            serviceNamesBuilder.Sort();
            var implementedServices = serviceNamesBuilder.Count == serviceNamesBuilder.Capacity ? serviceNamesBuilder.MoveToImmutable() : serviceNamesBuilder.ToImmutable();
            var serviceTypes = serviceTypesBuilder.MoveToImmutable();

            return new PublishedServices(serviceTypes, implementedServices);
        }

        private struct PublishedInstance
        {
            /// <summary>
            /// WeakReference or direct reference to instance.
            /// </summary>
            private readonly object instance;

            internal readonly bool Owned;

            public PublishedInstance(object instance, bool owned)
            {
                this.instance = instance;
                this.Owned = owned;
            }

            internal object? GetInstance()
            {
                if (this.instance is WeakReference wrInstance)
                {
                    return wrInstance.Target;
                }

                return this.instance;
            }
        }

        private readonly struct PublishedServices
        {
            internal readonly ImmutableArray<Type> ServiceTypes;

            internal readonly ImmutableArray<string> ServiceNames;

            internal PublishedServices(ImmutableArray<Type> serviceTypes, ImmutableArray<string> serviceNames)
            {
                this.ServiceTypes = serviceTypes;
                this.ServiceNames = serviceNames;
            }
        }

        private struct ServiceImplKey : IEquatable<ServiceImplKey>
        {
            internal readonly RpcObjectId objectId;

            internal readonly Type serviceType;

            public ServiceImplKey(RpcObjectId objectId, Type serviceType)
            {
                this.objectId = objectId;
                this.serviceType = serviceType;
            }

            public override bool Equals(object? obj)
            {
                return obj is ServiceImplKey other && this.Equals(other);
            }

            public bool Equals(ServiceImplKey other)
            {
                return this.objectId.Equals(other.objectId) && Equals(this.serviceType, other.serviceType);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = 47;
                    hashCode = (hashCode * 53) ^ EqualityComparer<RpcObjectId>.Default.GetHashCode(this.objectId);
                    if (this.serviceType != null)
                    {
                        hashCode = (hashCode * 53) ^ EqualityComparer<Type>.Default.GetHashCode(this.serviceType);
                    }

                    return hashCode;
                }
            }

        }

        private sealed class InstanceKey : IEquatable<InstanceKey>
        {
            private int hashCode;

            /// <summary>
            /// WeakReference or direct reference to instance.
            /// </summary>
            private object instance;

            internal InstanceKey(object instance, bool isWeak)
            {
                this.hashCode = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(instance);
                if (isWeak)
                {
                    this.instance = new WeakReference(instance);
                }
                else
                {
                    this.instance = instance;
                }
            }

            public bool Equals(InstanceKey other)
            {
                return this == other
                    || (other != null && this.hashCode == other.hashCode && this.GetInstance() == other.GetInstance());
            }

            public override bool Equals(object? obj)
            {
                return obj is InstanceKey other && this.Equals(other);
            }

            public override int GetHashCode() => this.hashCode;

            internal object? GetInstance()
            {
                if (this.instance is WeakReference wrInstance)
                {
                    return wrInstance.Target;
                }

                return this.instance;
            }

            internal PublishedInstance GetPublishedInstance(bool owned)
            {
                return new PublishedInstance(this.instance, owned);
            }

        }

        private sealed class ServiceActivator<TServiceImpl, TService>
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


    public class RpcServicePublisherOptions
    {

        public RpcServerId ServerId { get; set; }

    }

    public static class ServicePublisherExtensions
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
                    var publishedService = s != null ? servicePublisher.GetPublishedInstance<TService>(s) : null;
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
        /// Publishes an RPC singleton under the service name of the <typeparamref name="TService"/> RPC interface.
        /// The service instance will be created using the <see cref="IServiceProvider"/> associated with the RPC call.
        /// </summary>
        /// <typeparam name="TService">The interface of the service type. Must be an interface type with the <see cref="RpcServiceAttribute"/> (or <c>ServiceContractAttribute)</c>) 
        /// applied.</typeparam>
        /// <returns>A scoped object including the <see cref="RpcSingletonRef{TService}"/> identifying the published singleton. The scoped object will unpublish 
        /// the service singleton when disposed.</returns>
        public static ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TService>(this IRpcServicePublisher publisher)
            where TService : class
        {
            if (publisher is null) throw new ArgumentNullException(nameof(publisher));

            return publisher.PublishSingleton<TService, TService>();
        }

    }
}
