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
using SciTech.ComponentModel;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Server.Internal;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace SciTech.Rpc.Server
{
    /// <summary>
    /// <para>
    /// Default implementation of <see cref="IRpcServicePublisher"/>. 
    /// </para>
    /// <para>Normally it is only necessary to directly use this class when publishing the same set of services on
    /// multiple <see cref="IRpcServer"/>s. If an <see cref="IRpcServer"/> is created without providing an <see cref="IRpcServicePublisher" />
    /// a default <see cref="RpcServicePublisher"/>  will be created.
    /// </para>
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
        private readonly Dictionary<Type, Delegate> singletonServiceTypeToFactory = new Dictionary<Type, Delegate>();

        /// <summary>
        /// Maps a published singleton type to all RPC interface types published under that singleton.
        /// </summary>
        private readonly Dictionary<Type, PublishedServices> singletonTypeToPublishedServices = new Dictionary<Type, PublishedServices>();

        private readonly Dictionary<Type, PublishedInstance> singletonServiceTypeToServiceImpl = new Dictionary<Type, PublishedInstance>();

        private readonly object syncRoot = new object();

        private RpcConnectionInfo? connectionInfo;

        private bool connectionInfoRetrieved;

        private RpcServerId serverId;

        public RpcServicePublisher(IRpcServiceDefinitionsProvider serviceDefinitionsProvider, RpcServerId serverId = default)
        {
            this.DefinitionsProvider = serviceDefinitionsProvider ?? throw new ArgumentNullException(nameof(serviceDefinitionsProvider));
            this.serverId = serverId;
        }


        public RpcServicePublisher(RpcConnectionInfo connectionInfo, IRpcServiceDefinitionsProvider serviceDefinitionsProvider)
        {
            this.InitConnectionInfo(connectionInfo);
            this.DefinitionsProvider = serviceDefinitionsProvider ?? throw new ArgumentNullException(nameof(serviceDefinitionsProvider));
        }

        public RpcConnectionInfo? ConnectionInfo
        {
            get
            {
                lock (this.syncRoot)
                {
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

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Not owner")]
        public RpcObjectRef<TService> GetOrPublishInstance<TService>(TService serviceInstance) where TService : class
        {
            if (serviceInstance is null) throw new ArgumentNullException(nameof(serviceInstance));

            InstanceKey key;
            lock (this.syncRoot)
            {
                key = new InstanceKey(serviceInstance);
                if (this.serviceImplToId.TryGetValue(key, out var instanceId))
                {
                    return new RpcObjectRef<TService>(this.connectionInfo, instanceId, this.GetPublishedServices(instanceId).ToArray());
                }
            }

            // Not published, so we try to register the serviceInstance's service definitions 
            // and then publish it.

            var allServices = RpcBuilderUtil.GetAllServices(serviceInstance.GetType(), true);
            this.TryRegisterServiceDefinitions(allServices, null);

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
                var newPublishedServices = this.PublishInstanceCore_Locked(allServices, OwnedObject.CreateUnowned(serviceInstance), objectId, true);
                return new RpcObjectRef<TService>(connectionInfo, objectId, newPublishedServices.ToArray());
            }
        }

        public RpcObjectRef<TService>? GetPublishedInstance<TService>(TService serviceInstance) where TService : class
        {
            var connectionInfo = this.RetrieveConnectionInfo();

            lock (this.syncRoot)
            {
                var key = new InstanceKey(serviceInstance);
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
        /// <param name="connectionInfo"></param>
        /// <exception cref="InvalidOperationException">Thrown if the <see cref="ConnectionInfo"/> has already been retrieved.</exception>
        public void InitConnectionInfo(RpcConnectionInfo connectionInfo)
        {
            if (connectionInfo == null) throw new ArgumentNullException(nameof(connectionInfo));

            lock (this.syncRoot)
            {
                if (!Equals(this.connectionInfo, connectionInfo))
                {
                    if (this.connectionInfoRetrieved)
                    {
                        throw new InvalidOperationException("Cannot change ConnectionInfo after it has been retrieved.");
                    }

                    if (this.serverId != RpcServerId.Empty)
                    {
                        if (connectionInfo.ServerId == RpcServerId.Empty)
                        {
                            this.connectionInfo = connectionInfo.SetServerId(this.serverId);
                        }
                        else if (this.serverId != connectionInfo.ServerId)
                        {
                            throw new InvalidOperationException("Cannot change server id after it has been assigned.");
                        }
                    }
                    else
                    {
                        this.connectionInfo = connectionInfo;
                        this.serverId = connectionInfo.ServerId;
                    }
                }
            }
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public IOwned<RpcObjectRef<TService>> PublishInstance<TService>(Func<IServiceProvider?, RpcObjectId, IOwned<TService>> factory)
            where TService : class
        {
            var allServices = RpcBuilderUtil.GetAllServices(typeof(TService), RpcServiceDefinitionSide.Server, true);
            this.TryRegisterServiceDefinitions(allServices, null);

            var connectionInfo = this.RetrieveConnectionInfo();

            lock (this.syncRoot)
            {
                var objectId = RpcObjectId.NewId();

                var publishedServices = this.PublishInstanceFactoryCore_Locked(allServices, objectId, factory);

                return OwnedObject.Create(new RpcObjectRef<TService>(
                    connectionInfo, objectId, publishedServices.ToArray()), 
                    () => this.UnpublishInstanceAsync(objectId));

            }
        }

        public IOwned<RpcObjectRef<TService>> PublishInstance<TService>(IOwned<TService> serviceInstance)
            where TService : class
        {
            if (serviceInstance is null) throw new ArgumentNullException(nameof(serviceInstance));
            var allServices = RpcBuilderUtil.GetAllServices(serviceInstance.Value.GetType(), true);
            this.TryRegisterServiceDefinitions(allServices, null);

            var connectionInfo = this.RetrieveConnectionInfo();
            lock (this.syncRoot)
            {
                var serviceInstanceId = RpcObjectId.NewId();

                var publishedServices = this.PublishInstanceCore_Locked(allServices, serviceInstance, serviceInstanceId, false);

                Func<ValueTask> disposeAction = () => this.UnpublishInstanceAsync(serviceInstanceId);

                return OwnedObject.Create(new RpcObjectRef<TService>(
                    connectionInfo, serviceInstanceId, publishedServices.ToArray()), disposeAction);
            }
        }


        public IOwned<RpcSingletonRef<TService>> PublishSingleton<TService>(Func<IServiceProvider?, IOwned<TService>> factory)
            where TService : class
        {
            this.PublishSingletonFactoryCore(factory);

            return OwnedObject.Create(new RpcSingletonRef<TService>(
                this.RetrieveConnectionInfo()), 
                () => this.UnpublishSingletonAsync<TService>());
        }

        public IOwned<RpcSingletonRef<TService>> PublishSingleton<TService>(IOwned<TService> singletonService) where TService : class
        {
            if (singletonService == null) throw new ArgumentNullException(nameof(singletonService));

            var allServices = RpcBuilderUtil.GetAllServices(typeof(TService), false);
            this.TryRegisterServiceDefinitions(allServices, null);

            var publishedServices = this.VerifyPublishedServices(allServices);

            var connectionInfo = this.RetrieveConnectionInfo();
            lock (this.syncRoot)
            {
                var instanceKey = new InstanceKey(singletonService.Value);

                foreach (var serviceType in publishedServices.ServiceTypes)
                {
                    if (this.singletonServiceTypeToServiceImpl.ContainsKey(serviceType) || this.singletonServiceTypeToFactory.ContainsKey(serviceType))
                    {
                        throw new RpcDefinitionException($"A singleton for the type '{serviceType}' has already been published.");
                    }
                }

                this.singletonTypeToPublishedServices.Add(typeof(TService), publishedServices);
                var publishedInstance = new PublishedInstance(singletonService);
                foreach (var serviceType in publishedServices.ServiceTypes)
                {
                    this.singletonServiceTypeToServiceImpl.Add(serviceType, publishedInstance);
                }
            }

            return OwnedObject.Create(new RpcSingletonRef<TService>(
                connectionInfo), 
                () => this.UnpublishSingletonAsync<TService>());
        }

        public RpcConnectionInfo RetrieveConnectionInfo()
        {
            lock (this.syncRoot)
            {
                this.InitServerId();

                if (this.connectionInfo == null)
                {
                    this.connectionInfo = new RpcConnectionInfo("RpcServer", null, this.serverId);
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
        /// <param name="connectionInfo"></param>
        public RpcConnectionInfo TryInitConnectionInfo(RpcConnectionInfo connectionInfo)
        {
            if (connectionInfo == null)
            {
                throw new ArgumentNullException(nameof(connectionInfo));
            }

            lock (this.syncRoot)
            {
                if (!Equals(this.connectionInfo, connectionInfo))
                {
                    if (this.connectionInfo == null)
                    {
                        this.connectionInfo = connectionInfo;
                    }
                    else
                    {
                        // We already have a connection info. If it's missing a server id, let's update server id 
                        // if provided.
                        if (this.serverId == RpcServerId.Empty)
                        {
                            if (connectionInfo.ServerId != RpcServerId.Empty)
                            {
                                this.connectionInfo = this.connectionInfo.SetServerId(connectionInfo.ServerId);
                                this.serverId = connectionInfo.ServerId;
                            }
                        }
                        else
                        {
                            if (connectionInfo.ServerId != RpcServerId.Empty && connectionInfo.ServerId != this.serverId)
                            {
                                throw new InvalidOperationException("Server id of provided connection does not match already assigned server id.");
                            }
                        }
                    }
                }

                return this.connectionInfo!;
            }
        }

        public ValueTask UnpublishInstanceAsync(RpcObjectId serviceInstanceId)
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
                        this.serviceImplToId.Remove(new InstanceKey(instance));
                    }
                }
            }

            if (removedInstance?.GetOwnedInstance() is IAsyncDisposable disposable)
            {
                return disposable.DisposeAsync();
            }

            return default;
        }

        public ValueTask UnpublishSingletonAsync<TService>() where TService : class
        {
            PublishedInstance? removedInstance = null;

            lock (this.syncRoot)
            {
                if (this.singletonTypeToPublishedServices.TryGetValue(typeof(TService), out var publishedTypes))
                {
                    foreach (var serviceType in publishedTypes.ServiceTypes)
                    {
                        this.singletonServiceTypeToFactory.Remove(serviceType);
                        if (this.singletonServiceTypeToServiceImpl.TryGetValue(serviceType, out var publishedInstance))
                        {
                            this.singletonServiceTypeToServiceImpl.Remove(serviceType);
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

            if (removedInstance?.GetOwnedInstance() is IAsyncDisposable disposable)
            {
                return disposable.DisposeAsync();
            }

            return default;
        }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Not owner")]
        ActivatedService<TService> IRpcServiceActivator.GetActivatedService<TService>(IServiceProvider? serviceProvider, RpcObjectId id) where TService : class
        {
            var key = new ServiceImplKey(id, typeof(TService));
            lock (this.syncRoot)
            {
                if (id != RpcObjectId.Empty)
                {
                    if (this.idToServiceImpl.TryGetValue(key, out var serviceImpl) )
                    {
                        if( RpcStubOptions.ForceCollectActivatedInstance && serviceImpl.IsWeak && serviceImpl.GetInstance() is TService  )
                        {
                            GC.Collect();
                        }

                        if (serviceImpl.GetInstance() is TService service)
                        {
                            return new ActivatedService<TService>(service, null);
                        }

                        return default;
                    }

                    if (this.idToServiceFactory.TryGetValue(key, out var serviceFactory))
                    {
                        var ownedInstance = ((Func<IServiceProvider?, RpcObjectId, IOwned<TService>>)serviceFactory)(serviceProvider, id);
                        if( ownedInstance != null )
                        {
                            return new ActivatedService<TService>(ownedInstance.Value, ownedInstance.CanDispose ? ownedInstance : null);
                        }
                    }
                }
                else
                {
                    if (this.singletonServiceTypeToServiceImpl.TryGetValue(typeof(TService), out var serviceImpl) 
                        && serviceImpl.GetInstance() is TService service)
                    {
                        return new ActivatedService<TService>(service, null);
                    }

                    if (this.singletonServiceTypeToFactory.TryGetValue(typeof(TService), out var singletonfactory))
                    {
                        var ownedInstance = ((Func<IServiceProvider?, IOwned<TService>>)singletonfactory)(serviceProvider);
                        if (ownedInstance != null)
                        {
                            return new ActivatedService<TService>(ownedInstance.Value, ownedInstance.CanDispose ? ownedInstance : null);
                        }
                    }
                }
            }

            return default;
        }

        bool IRpcServiceActivator.CanGetActivatedService<TService>(RpcObjectId id) where TService : class
        {
            var key = new ServiceImplKey(id, typeof(TService));
            lock (this.syncRoot)
            {
                if (id != RpcObjectId.Empty)
                {
                    return 
                        (this.idToServiceImpl.TryGetValue(key, out var serviceImpl) && serviceImpl.GetInstance() is TService )
                        || this.idToServiceFactory.ContainsKey(key);
                }
                else
                {
                    return (this.singletonServiceTypeToServiceImpl.TryGetValue(typeof(TService), out var serviceImpl) && serviceImpl.GetInstance() is TService )
                        || this.singletonServiceTypeToFactory.ContainsKey(typeof(TService));
                }
            }
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
            IOwned<object> serviceInstance,
            RpcObjectId serviceInstanceId,
            bool isWeak)
        {
            Debug.Assert(serviceInstanceId != RpcObjectId.Empty);


            InstanceKey key;
            WeakReference? wrInstance = isWeak ? new WeakReference(serviceInstance.Value) : null;

            key = new InstanceKey(wrInstance, serviceInstance.Value);

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
                var publishedInstance = wrInstance != null ? 
                    new PublishedInstance(wrInstance) 
                    : new PublishedInstance(serviceInstance);

                foreach (var serviceType in publishedServices.ServiceTypes)
                {
                    this.idToServiceImpl.Add(new ServiceImplKey(serviceInstanceId, serviceType), publishedInstance);
                }

                this.idToPublishedServices.Add(serviceInstanceId, publishedServices);
            }

            this.serviceImplToId.Add(key, serviceInstanceId);

            return publishedServices.ServiceNames;
        }

        private ImmutableArray<string> PublishInstanceFactoryCore_Locked<TService>(IReadOnlyList<RpcServiceInfo> allServices, RpcObjectId objectId, Func<IServiceProvider, RpcObjectId, IOwned<TService>> factory)
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

        private ImmutableArray<string> PublishSingletonFactoryCore<TService>(Func<IServiceProvider?, IOwned<TService>> factory)
            where TService : class
        {
            // Getting the ServiceInfo validates that TService is actually an RPC service interface.
            RpcBuilderUtil.GetServiceInfoFromType(typeof(TService));
            this.TryRegisterServiceDefinition(typeof(TService));

            var allServices = RpcBuilderUtil.GetAllServices(typeof(TService), RpcServiceDefinitionSide.Server, true);
            var publishedServices = this.VerifyPublishedServices(allServices);

            lock (this.syncRoot)
            {
                foreach (var serviceType in publishedServices.ServiceTypes)
                {
                    if (this.singletonServiceTypeToFactory.ContainsKey(serviceType) || this.singletonServiceTypeToServiceImpl.ContainsKey(serviceType))
                    {
                        throw new RpcDefinitionException($"A singleton for the type '{serviceType}' has already been published.");
                    }
                }

                this.singletonTypeToPublishedServices.Add(typeof(TService), publishedServices);
                foreach (var serviceType in publishedServices.ServiceTypes)
                {
                    this.singletonServiceTypeToFactory.Add(serviceType, factory);
                }
            }

            return publishedServices.ServiceNames;
        }

        private void TryRegisterServiceDefinition(Type serviceType)
        {
            if (this.DefinitionsProvider is IRpcServiceDefinitionsBuilder builder)
            {
                if (!this.DefinitionsProvider.IsFrozen)
                {
                    if (!this.DefinitionsProvider.IsServiceRegistered(serviceType))
                    {
                        builder.RegisterService(serviceType, null);
                    }
                }
            }
        }

        /// <summary>
        /// Tries to registered the provided services. This will succeed if the <see cref="DefinitionsProvider"/> implements
        /// <see cref="IRpcServiceDefinitionsBuilder"/> and the builder is not frozen.
        /// </summary>
        /// <param name="allServices"></param>
        /// <param name="implementationType"></param>
        private void TryRegisterServiceDefinitions(IReadOnlyList<RpcServiceInfo> allServices, Type? implementationType)
        {
            if (this.DefinitionsProvider is IRpcServiceDefinitionsBuilder builder)
            {
                if (!this.DefinitionsProvider.IsFrozen)
                {
                    foreach (var service in allServices)
                    {
                        if (!this.DefinitionsProvider.IsServiceRegistered(service.Type))
                        {
                            builder.RegisterService(service.Type, implementationType);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Verifies that all provided services are registered with the <see cref="DefinitionsProvider"/> and 
        /// returns names and types of the services.
        /// </summary>
        /// <param name="allServices"></param>
        /// <returns></returns>
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

        IImmutableList<Type> IRpcServiceActivator.GetPublishedSingletons()
        {
            lock( this.syncRoot)
            {
                return this.singletonTypeToPublishedServices.Keys.ToImmutableArray();
            }
        }

        private struct PublishedInstance
        {            
            /// <summary>
            /// WeakReference to instance or direct reference to IOwned instance.
            /// </summary>
            private readonly object instance;

            internal PublishedInstance(IOwned<object> instance)
            {
                this.instance = instance;
            }

            internal PublishedInstance(WeakReference wrInstance)
            {
                this.instance = wrInstance;
            }

            internal IOwned<object>? GetOwnedInstance()
            {
                return this.instance as IOwned<object>;
            }

            internal object? GetInstance()
            {
                if (this.instance is WeakReference wrInstance)
                {
                    return wrInstance.Target;
                }

                return (this.instance as IOwned<object>)?.Value;
            }

            internal bool IsWeak => this.instance is WeakReference;
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

            internal InstanceKey(object instance)
            {
                this.hashCode = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(instance);
                this.instance = instance;
            }

            internal InstanceKey(WeakReference? wrInstance, object instance)
            {
                this.hashCode = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(instance);
                this.instance = wrInstance ?? instance;
            }


            public bool Equals([AllowNull] InstanceKey other)
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

            internal WeakReference? WeakInstance => this.instance as WeakReference;

        }
    }


    public class RpcServicePublisherOptions
    {

        public RpcServerId ServerId { get; set; }

    }
}
