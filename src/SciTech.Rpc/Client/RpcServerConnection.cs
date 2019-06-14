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

using SciTech.Rpc.Client;
using SciTech.Rpc.Client.Internal;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Client
{
    /// <summary>
    /// Base implementation of the <see cref="IRpcServerConnection"/> interface.
    /// </summary>
    public abstract class RpcServerConnection : IRpcServerConnection
    {
        private readonly RpcProxyProvider proxyProvider;

        private readonly Dictionary<RpcObjectId, List<WeakReference<RpcProxyBase>>> serviceInstances = new Dictionary<RpcObjectId, List<WeakReference<RpcProxyBase>>>();

        private readonly object syncRoot = new object();

        protected RpcServerConnection(RpcServerConnectionInfo connectionInfo, RpcProxyProvider proxyProvider)
        {
            this.ConnectionInfo = connectionInfo;
            this.proxyProvider = proxyProvider;
        }

        /// <summary>
        /// Gets the connection info of this connection.
        /// </summary>
        public RpcServerConnectionInfo ConnectionInfo { get; }

        public TService GetServiceInstance<TService>(RpcObjectId objectId, IReadOnlyCollection<string>? implementedServices, SynchronizationContext? syncContext) where TService : class
        {
            if (objectId == RpcObjectId.Empty)
            {
                throw new ArgumentException("ObjectId should not be empty.", nameof(objectId));
            }

            return GetServiceInstanceCore<TService>(objectId, implementedServices, syncContext);
        }

        public TService GetServiceSingleton<TService>(SynchronizationContext? syncContext) where TService : class
        {
            // TODO: Implement singleton factories.

            return GetServiceInstanceCore<TService>(RpcObjectId.Empty, syncContext);
        }

        public abstract Task ShutdownAsync();

        private TService GetServiceInstanceCore<TService>(RpcObjectId refObjectId, SynchronizationContext? syncContext) where TService : class
        {
            return GetServiceInstanceCore<TService>(refObjectId, default, syncContext);
        }

        private TService GetServiceInstanceCore<TService>(
            RpcObjectId refObjectId,
            IReadOnlyCollection<string>? implementedServices,
            SynchronizationContext? syncContext) where TService : class
        {
            lock (this.syncRoot)
            {
                if (this.serviceInstances.TryGetValue(refObjectId, out var servicesList))
                {
                    foreach (var wService in servicesList)
                    {
                        if (wService.TryGetTarget(out var proxyBase)
                            && proxyBase is TService service
                            && proxyBase.SyncContext == syncContext
                            && proxyBase.ImplementsServices(implementedServices))
                        {
                            return service;
                        }
                    }
                }
            }

            RpcObjectProxyFactory serviceProxyCreator
                = this.proxyProvider.ProxyGenerator.GenerateObjectProxyFactory<TService>(implementedServices);

            lock (this.syncRoot)
            {
                if (this.serviceInstances.TryGetValue(refObjectId, out var servicesList))
                {
                    foreach (var wService in servicesList)
                    {
                        if (wService.TryGetTarget(out var proxyBase)
                            && proxyBase is TService service
                            && proxyBase.SyncContext == syncContext
                            && proxyBase.ImplementsServices(implementedServices))
                        {
                            return service;
                        }
                    }
                }
                else
                {
                    servicesList = new List<WeakReference<RpcProxyBase>>();
                    this.serviceInstances.Add(refObjectId, servicesList);
                }

                var serviceInstance = serviceProxyCreator(refObjectId, this, syncContext);
                servicesList.Add(new WeakReference<RpcProxyBase>(serviceInstance));

                return (TService)(object)serviceInstance;
            }
        }
    }
}
