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

using SciTech.Rpc.Server.Internal;
using System;
using System.ComponentModel;
using System.Linq;

namespace SciTech.Rpc.Server
{
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

        ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TService>(Func<IServiceProvider, TService> factory)
            where TService : class;

        [EditorBrowsable(EditorBrowsableState.Never)]
        ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TService>(Func<IServiceProvider?, ActivatedService<TService>> factory)
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
}
