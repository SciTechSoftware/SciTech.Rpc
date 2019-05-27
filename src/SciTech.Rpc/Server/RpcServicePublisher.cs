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

using SciTech.Rpc.Internal;
using SciTech.Rpc.Server.Internal;
using System;
using System.Collections.Generic;
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
        /// <typeparam name="TService">The </typeparam>
        /// <param name="factory">A factory function that should create the service instance specified by the <see cref="RpcObjectId"/>
        /// with the help of the provided <see cref="IServiceProvider"/>.</param>
        /// <returns>A scoped object including the <see cref="RpcObjectRef"/> identifying the published instannce. The scoped object will unpublish 
        /// the service instance when disposed.</returns>
        ScopedObject<RpcObjectRef<TService>> PublishInstance<TService>(Func<IServiceProvider, RpcObjectId, TService> factory) where TService : class;

        /// <summary>
        /// Publishes an RPC service instance.
        /// </summary>
        /// <typeparam name="TService"></typeparam>
        /// <param name="serviceInstance">The </param>
        /// <param name="takeOwnership"><c>true</c> to indicate that the instance should be disposed when unpublished.</param>
        /// <returns>A scoped object including the <see cref="RpcObjectRef"/> identifying the published instannce. The scoped object will unpublish 
        /// the service instance when disposed.</returns>
        ScopedObject<RpcObjectRef<TService>> PublishInstance<TService>(TService serviceInstance, bool takeOwnership = false) where TService : class;

        /// <summary>
        /// Publishes an RPC singleton under the service name of the <typeparamref name="TService"/> RPC interface.
        /// The service instance will be created using the <see cref="IServiceProvider"/> associated with the RPC call.
        /// </summary>
        /// <typeparam name="TServiceImpl">The type of the service implementation. This type will be used when resolving the service implementation using 
        /// the <see cref="IServiceProvider"/> associated with the RPC call.
        /// </typeparam>
        /// <typeparam name="TService">The interface of the service type. Must be an interface type with the <see cref="RpcServiceAttribute"/> applied.</typeparam>
        /// <returns>A scoped object including the <see cref="RpcSingletonRef{TService}"/> identifying the published singleton. The scoped object will unpublish 
        /// the service singleton when disposed.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TServiceImpl, TService>()
            where TService : class
            where TServiceImpl : class, TService;

        ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TService>(Func<IServiceProvider, TService> factory)
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

        // TODO: Unpublish methods should probably be removed, unpublish by disposing returned Rpc...Ref
        void UnpublishServiceInstance(RpcObjectId serviceInstanceId);

        // TODO: Unpublish methods should probably be removed, unpublish by disposing returned Rpc...Ref
        void UnpublishSingleton<TService>() where TService : class;
    }

    public sealed class RpcServicePublisher : IRpcServicePublisher, IRpcServiceActivator
    {
        private readonly Dictionary<RpcObjectId, IReadOnlyList<string>> idToPublishedServices = new Dictionary<RpcObjectId, IReadOnlyList<string>>();

        private readonly Dictionary<ServiceImplKey, Func<IServiceProvider, RpcObjectId, object>> idToServiceFactory
            = new Dictionary<ServiceImplKey, Func<IServiceProvider, RpcObjectId, object>>();

        private readonly Dictionary<ServiceImplKey, InstanceKey> idToServiceImpl = new Dictionary<ServiceImplKey, InstanceKey>();

        private readonly Dictionary<InstanceKey, RpcObjectId> serviceImplToId = new Dictionary<InstanceKey, RpcObjectId>();

        private readonly object syncRoot = new object();

        private readonly Dictionary<Type, Func<IServiceProvider, object>> typeToSingletonServiceFactory
            = new Dictionary<Type, Func<IServiceProvider, object>>();

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
                    this.idToPublishedServices.TryGetValue(instanceId, out var publishedServices);
                    return new RpcObjectRef<TService>(this.connectionInfo, instanceId, publishedServices?.ToArray());
                }
            }

            // Not published, so we try to register the serviceInstance's service definitions 
            // and then publish it.

            var allServices = RpcBuilderUtil.GetAllServices(serviceInstance.GetType(), true);
            this.TryRegisterServiceDefinitions(allServices);

            lock (this.syncRoot)
            {
                // Let's try again.
                if (this.serviceImplToId.TryGetValue(key, out var instanceId))
                {
                    // Somebody beat us to it.
                    this.idToPublishedServices.TryGetValue(instanceId, out var publishedServices);
                    return new RpcObjectRef<TService>(this.connectionInfo, instanceId, publishedServices?.ToArray());
                }

                var objectId = RpcObjectId.NewId();
                var newPublishedServices = this.PublishServiceInstanceCore(allServices, serviceInstance, objectId, true);
                return new RpcObjectRef<TService>(this.connectionInfo, objectId, newPublishedServices.ToArray());
            }
        }

        public RpcObjectRef<TService>? GetPublishedInstance<TService>(TService serviceInstance) where TService : class
        {
            lock (this.syncRoot)
            {
                var key = new InstanceKey(serviceInstance, false);
                if (this.serviceImplToId.TryGetValue(key, out var objectId))
                {
                    this.idToPublishedServices.TryGetValue(objectId, out var publishedServices);
                    return new RpcObjectRef<TService>(this.RetrieveConnectionInfo(), objectId, publishedServices.ToArray());
                }
            }

            return null;
        }

        public IReadOnlyList<string> GetPublishedServices(RpcObjectId objectId)
        {
            lock (this.syncRoot)
            {
                this.idToPublishedServices.TryGetValue(objectId, out var servicesList);
                return servicesList ?? Array.Empty<string>();
            }
        }

        /// <summary>
        /// TODO: This should be an explicit interface member, but due to changes 
        /// between Visual Studio 2019 and the upcoming preview of Visual Studio 2019 16.1 this
        /// doesn't work. Should be made internal somehow.
        /// </summary>
        /// <typeparam name="TService"></typeparam>
        /// <param name="serviceProvider"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        public TService? GetServiceImpl<TService>(IServiceProvider? serviceProvider, RpcObjectId id) where TService : class
        {
            var key = new ServiceImplKey(id, typeof(TService));
            lock (this.syncRoot)
            {
                if (this.idToServiceImpl.TryGetValue(key, out var serviceImpl) && serviceImpl.GetInstance() is TService service)
                {
                    return service;
                }

                if (id != RpcObjectId.Empty)
                {
                    if (this.idToServiceFactory.TryGetValue(key, out var serviceFactory))
                    {
                        if (serviceProvider == null)
                        {
                            // TODO: At least log, maybe throw?
                            return null;
                        }

                        return (TService)serviceFactory(serviceProvider, id);
                    }
                }
                else
                {
                    if (this.typeToSingletonServiceFactory.TryGetValue(typeof(TService), out var singletonfactory))
                    {
                        if (serviceProvider == null)
                        {
                            // TODO: At least log, maybe throw?
                            return null;
                        }

                        return (TService)singletonfactory(serviceProvider);
                    }
                }
            }

            return null;
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
            var allServices = RpcBuilderUtil.GetAllServices(typeof(TService), true);
            this.TryRegisterServiceDefinitions(allServices);

            lock (this.syncRoot)
            {
                RpcObjectId objectId = RpcObjectId.NewId();

                var publishedServices = this.PublishInstanceFactoryCore(allServices, objectId, factory);

                return new ScopedObject<RpcObjectRef<TService>>(new RpcObjectRef<TService>(
                    this.RetrieveConnectionInfo(), objectId, publishedServices.ToArray()), () => this.UnpublishServiceInstance(objectId));

            }
        }

        public ScopedObject<RpcObjectRef<TService>> PublishInstance<TService>(TService serviceInstance, bool takeOwnership = false)
            where TService : class
        {
            if (serviceInstance is null) throw new ArgumentNullException(nameof(serviceInstance));

            lock (this.syncRoot)
            {
                var serviceInstanceId = RpcObjectId.NewId();

                var allServices = RpcBuilderUtil.GetAllServices(serviceInstance.GetType(), true);
                var publishedServices = this.PublishServiceInstanceCore(allServices, serviceInstance, serviceInstanceId, false);

                Action disposeAction;
                if (takeOwnership && serviceInstance is IDisposable disposableService)
                {
                    disposeAction = () =>
                    {
                        this.UnpublishServiceInstance(serviceInstanceId);
                        disposableService.Dispose();
                    };
                }
                else
                {
                    disposeAction = () => this.UnpublishServiceInstance(serviceInstanceId);
                }

                return new ScopedObject<RpcObjectRef<TService>>(new RpcObjectRef<TService>(
                    this.RetrieveConnectionInfo(), serviceInstanceId, publishedServices.ToArray()), disposeAction);
            }
        }

        public ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TServiceImpl, TService>()
            where TServiceImpl : class, TService
            where TService : class
        {
            // TODO: Should use ObjectFactory if s.GetService cannot resolve TServiceImpl.
            return PublishSingleton<TService>(s => (TService)s.GetService(typeof(TServiceImpl)));
        }

        public ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TService>(Func<IServiceProvider, TService> factory)
            where TService : class
        {
            var rpcServiceType = RpcBuilderUtil.GetServiceInfoFromType(typeof(TService));
            var rpcTypesList = new RpcServiceInfo[] { rpcServiceType };
            this.TryRegisterServiceDefinitions(rpcTypesList);

            lock (this.syncRoot)
            {
                this.PublishSingletonFactoryCore(rpcTypesList, factory);

                return new ScopedObject<RpcSingletonRef<TService>>(new RpcSingletonRef<TService>(
                    this.RetrieveConnectionInfo()), () => this.UnpublishSingleton<TService>());

            }
        }

        public ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TService>(TService singletonService, bool takeOwnership = false) where TService : class
        {
            var rpcServiceType = RpcBuilderUtil.GetServiceInfoFromType(typeof(TService));
            var rpcTypesList = new RpcServiceInfo[] { rpcServiceType };
            this.TryRegisterServiceDefinitions(rpcTypesList);

            lock (this.syncRoot)
            {
                this.PublishServiceInstanceCore(rpcTypesList, singletonService, RpcObjectId.Empty, false);
                return new ScopedObject<RpcSingletonRef<TService>>(new RpcSingletonRef<TService>(
                    this.RetrieveConnectionInfo()), () => this.UnpublishSingleton<TService>());

            }
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

        public void UnpublishServiceInstance(RpcObjectId serviceInstanceId)
        {
            lock (this.syncRoot)
            {
                //throw new NotImplementedException();
                //this.idToServiceImpl.Remove(serviceInstanceId);
            }
        }

        public void UnpublishSingleton<TService>() where TService : class
        {
            lock (this.syncRoot)
            {
                //throw new NotImplementedException();
                //this.idToServiceImpl.Remove(serviceInstanceId);
            }
        }

        private void InitServerId()
        {
            if (this.serverId == RpcServerId.Empty)
            {
                this.serverId = RpcServerId.NewId();
            }
        }

        private IReadOnlyCollection<string> PublishInstanceFactoryCore(IReadOnlyList<RpcServiceInfo> allServices, RpcObjectId objectId, Func<IServiceProvider, RpcObjectId, object> factory)
        {
            string[] implementedServices = this.VerifyPublishedServices(allServices);

            foreach (var serviceInfo in allServices)
            {
                this.idToServiceFactory.Add(new ServiceImplKey(objectId, serviceInfo.Type), factory);
            }

            Array.Sort(implementedServices);

            return implementedServices;
        }

        private IReadOnlyCollection<string> PublishServiceInstanceCore(IReadOnlyList<RpcServiceInfo> allServices, object serviceInstance, RpcObjectId serviceInstanceId, bool isWeak)
        {
            var key = new InstanceKey(serviceInstance, isWeak);
            if (this.serviceImplToId.ContainsKey(key))
            {
                throw new InvalidOperationException("Service instance already published.");
            }

            if (allServices.Count == 0)
            {
                throw new ArgumentException("The published instance does not implement any RPC service interface.", nameof(serviceInstance));
            }

            string[] implementedServices = new string[allServices.Count];

            int di = 0;
            foreach (var serviceInfo in allServices)
            {
                if (!this.DefinitionsProvider.IsServiceRegistered(serviceInfo.Type))
                {
                    throw new RpcDefinitionException($"Published service '{serviceInfo.Type}' is not registered.");
                }

                if (Array.Find(implementedServices, s => s == serviceInfo.FullName) == null)
                {
                    implementedServices[di++] = serviceInfo.FullName;
                }
            }

            foreach (var serviceInfo in allServices)
            {
                this.idToServiceImpl.Add(new ServiceImplKey(serviceInstanceId, serviceInfo.Type), key);
            }

            Array.Sort(implementedServices);

            if (serviceInstanceId != RpcObjectId.Empty)
            {
                this.idToPublishedServices.Add(serviceInstanceId, implementedServices);
            }

            this.serviceImplToId.Add(key, serviceInstanceId);

            return implementedServices;
        }

        private IReadOnlyCollection<string> PublishSingletonFactoryCore(IReadOnlyList<RpcServiceInfo> allServices, Func<IServiceProvider, object> factory)
        {
            string[] implementedServices = this.VerifyPublishedServices(allServices);

            foreach (var serviceInfo in allServices)
            {
                this.typeToSingletonServiceFactory.Add(serviceInfo.Type, factory);
            }

            Array.Sort(implementedServices);

            return implementedServices;
        }

        private void TryRegisterServiceDefinitions(IReadOnlyList<RpcServiceInfo> allServices)
        {
            if (this.DefinitionsProvider is IRpcServiceDefinitionBuilder builder)
            {
                if (!builder.IsFrozen)
                {
                    foreach (var service in allServices)
                    {
                        builder.RegisterService(service.Type);
                    }
                }
            }
        }

        private string[] VerifyPublishedServices(IReadOnlyList<RpcServiceInfo> allServices)
        {
            if (allServices.Count == 0)
            {
                throw new ArgumentException("The published service type does not implement any RPC service interface.");
            }

            string[] implementedServices = new string[allServices.Count];

            int di = 0;
            foreach (var serviceInfo in allServices)
            {
                if (!this.DefinitionsProvider.IsServiceRegistered(serviceInfo.Type))
                {
                    throw new RpcDefinitionException($"Published service '{serviceInfo.Type}' is not registered.");
                }

                if (Array.Find(implementedServices, s => s == serviceInfo.FullName) == null)
                {
                    implementedServices[di++] = serviceInfo.FullName;
                }
            }

            return implementedServices;
        }

        private struct ServiceImplKey : IEquatable<ServiceImplKey>
        {
            private readonly RpcObjectId objectId;

            private readonly Type serviceType;

            public ServiceImplKey(RpcObjectId objectId, Type serviceType)
            {
                this.objectId = objectId;
                this.serviceType = serviceType;
            }

            public override bool Equals(object obj)
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

            public override bool Equals(object obj)
            {
                return obj is InstanceKey other && this.Equals(other);
            }

            public override int GetHashCode() => this.hashCode;

            internal object GetInstance()
            {
                if (this.instance is WeakReference wrInstance)
                {
                    return wrInstance.Target;
                }

                return this.instance;
            }
        }
    }

    public static class ServicePublisherExtensions
    {
        public static IList<RpcObjectRef<TService>?> GetPublishedServiceInstances<TService>(this IRpcServicePublisher servicePublisher,
            IReadOnlyList<TService> serviceInstances, bool allowUnpublished) where TService : class
        {
            if (servicePublisher is null) throw new ArgumentNullException(nameof(servicePublisher));

            if ( serviceInstances == null )
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
        /// <typeparam name="TService">The interface of the service type. Must be an interface type with the <see cref="RpcServiceAttribute"/> applied.</typeparam>
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
