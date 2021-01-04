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

using SciTech.ComponentModel;
using SciTech.Rpc.Server.Internal;
using System;
using System.ComponentModel;
using System.Linq;

namespace SciTech.Rpc.Server
{
    /// <summary>
    /// <para>
    /// Provides functionality to publish RPC service object instances and singleton instances. 
    /// </para>
    /// <para>Normally it is only necessary to use this interface when publishing the same set of services on
    /// multiple <see cref="IRpcServer"/>s. The <see cref="RpcServerExtensions"/> class provides extensions methods
    /// that can be used to publish RPC services directly on an <see cref="IRpcServer"/> interface.
    /// </para>
    /// <note type="note">
    /// <see cref="RpcServicePublisherExtensions"/> provides extension methods necessary for publishing RPC 
    /// singletons and instances using factory methods. Include the namespace <c>SciTech.Rpc.Server</c> for full
    /// functionality when publishing RPC services.
    /// </note>
    /// </summary>
    public interface IRpcServicePublisher
    {
        /// <summary>
        /// Gets the connection information associated with this publisher. This will be used
        /// to provide connection information for <see cref="RpcObjectRef"/>s and <see cref="RpcSingletonRef{TService}"/>s
        /// returned from the server.
        /// </summary>
        RpcServerConnectionInfo? ConnectionInfo { get; }

        /// <summary>
        /// Gets the server id with this publisher. If <see cref="ConnectionInfo"/> has been initialized this
        /// will be the same id as <see cref="RpcServerConnectionInfo.ServerId">ConnectionInfo.ServerId</see>.
        /// </summary>
        RpcServerId ServerId { get; }

        /// <summary>
        /// Get the <see cref="RpcObjectRef{TService}"/> to the published <paramref name="serviceInstance"/>, if it has been previously published.
        /// If it has not been previously published, it will be published as a weak service. A weak service will be automatically unpublished if the <paramref name="serviceInstance"/>
        /// is garbage collected.
        /// <para>To keep the published instance alive, it needs to be published using <see cref="PublishInstance{TService}(TService, bool)"/>.
        /// </para>
        /// </summary>
        /// <typeparam name="TService">Type of the service interface.</typeparam>
        /// <param name="serviceInstance">The service instance for which the <see cref="RpcObjectRef{TService}"/> should be retrieved.</param>
        /// <returns>The <see cref="RpcObjectRef{TService}"/> of the published instance.</returns>
        RpcObjectRef<TService> GetOrPublishInstance<TService>(TService serviceInstance) where TService : class;

        /// <summary>
        /// Get the <see cref="RpcObjectRef{TService}"/> to the published <paramref name="serviceInstance"/>, if it has been previously published.
        /// </summary>
        /// <typeparam name="TService">Type of the service interface.</typeparam>
        /// <param name="serviceInstance">The service instance for which the <see cref="RpcObjectRef{TService}"/> should be retrieved.</param>
        /// <returns>The <see cref="RpcObjectRef{TService}"/> of the published instance, if available; <c>null</c> otherwise.</returns>
        RpcObjectRef<TService>? GetPublishedInstance<TService>(TService serviceInstance) where TService : class;

        void InitConnectionInfo(RpcServerConnectionInfo connectionInfo);

        /// <summary>
        /// <para>
        /// Publishes an RPC service instance with the help of an <see cref="ActivatedService{TService}"/>  factory.
        /// </para>
        /// <note type="note">
        /// NOTE! The <see cref="ActivatedService{TService}"/> factory is an implementation detail. It is recommended that 
        /// the extension methods <see cref="RpcServicePublisherExtensions.PublishInstance{TService}(IRpcServicePublisher, Func{IServiceProvider, RpcObjectId, TService})"/> 
        /// or <see cref="RpcServicePublisherExtensions.PublishInstance{TService}(IRpcServicePublisher, Func{RpcObjectId, TService})"/>
        /// are used instead of this method.
        /// </note>
        /// </summary>
        /// <typeparam name="TService">The type of the published instance.</typeparam>
        /// <param name="factory">A factory function that should create the service instance specified by the <see cref="RpcObjectId"/>
        /// with the help of the provided <see cref="IServiceProvider"/>.</param>
        /// <returns>A scoped object including the <see cref="RpcObjectRef"/> identifying the published instance. The scoped object will unpublish 
        /// the service instance when disposed.</returns>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public IOwned<RpcObjectRef<TService>> PublishInstance<TService>(Func<IServiceProvider?, RpcObjectId, ActivatedService<TService>> factory)
            where TService : class;


        /// <summary>
        /// <para>
        /// Publishes an RPC service instance.
        /// </para>
        /// <note type="note">
        /// This service publisher will keep a strong reference to the published instance. To allow it to be garbage
        /// collected, it must be explicitly unpublished by disposing the returned object.<br/>
        /// To publish an instance that will be automatically unpublished when it is garbage collected, 
        /// use <see cref="GetOrPublishInstance{TService}(TService)"/>
        /// </note>
        /// </summary>
        /// <typeparam name="TService"></typeparam>
        /// <param name="serviceInstance">The </param>
        /// <param name="takeOwnership"><c>true</c> to indicate that the instance should be disposed when unpublished.</param>
        /// <returns>A scoped object including the <see cref="RpcObjectRef"/> identifying the published instance. The scoped object will unpublish 
        /// the service instance when disposed.</returns>
        IOwned<RpcObjectRef<TService>> PublishInstance<TService>(TService serviceInstance, bool takeOwnership = false) where TService : class;

        [EditorBrowsable(EditorBrowsableState.Never)]
        IOwned<RpcSingletonRef<TService>> PublishSingleton<TService>(Func<IServiceProvider?, ActivatedService<TService>> factory)
            where TService : class;

        IOwned<RpcSingletonRef<TService>> PublishSingleton<TService>(TService singletonService, bool takeOwnership = false) where TService : class;

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
