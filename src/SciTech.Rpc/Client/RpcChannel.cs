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

using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Serialization;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Client
{
    /// <summary>
    /// Base implementation of <see cref="IRpcChannel"/>.
    /// </summary>
    public abstract class RpcChannel : IRpcChannel
    {
        //private static readonly ILog Logger = LogProvider.For<RpcChannel>();

        private readonly IRpcProxyGenerator proxyGenerator;

        private readonly Dictionary<RpcObjectId, List<WeakReference<RpcProxyBase>>> serviceInstances
            = new Dictionary<RpcObjectId, List<WeakReference<RpcProxyBase>>>();

        private bool isDisposed;

        private volatile IRpcSerializer? serializer;

        protected RpcChannel(
            RpcServerConnectionInfo connectionInfo,
            IRpcClientOptions? options,
            IRpcProxyGenerator proxyGenerator)
        {
            this.ConnectionInfo = connectionInfo;
            this.proxyGenerator = proxyGenerator;
            this.Options = options?.AsImmutable() ?? ImmutableRpcClientOptions.Empty;
        }

        /// <inheritdoc/>
        public RpcServerConnectionInfo ConnectionInfo { get; }

        /// <inheritdoc/>
        public ImmutableRpcClientOptions Options { get; }

        protected internal IRpcSerializer Serializer
        {
            get
            {
                if (this.serializer == null)
                {
                    this.serializer = this.Options.Serializer ?? this.CreateDefaultSerializer();
                }

                return this.serializer;
            }
        }

        protected object SyncRoot { get; } = new object();

        public void Dispose()
        {
            if (!this.isDisposed)
            {
                this.Dispose(true);

                this.isDisposed = true;
            }
        }


        public TService GetServiceInstance<TService>(RpcObjectId objectId,
            IReadOnlyCollection<string>? implementedServices, SynchronizationContext? syncContext) where TService : class
        {
            if (objectId == RpcObjectId.Empty)
            {
                throw new ArgumentException("ObjectId should not be empty.", nameof(objectId));
            }

            return GetServiceInstanceCore<TService>(objectId, implementedServices, syncContext);
        }

        public TService GetServiceSingleton<TService>(SynchronizationContext? syncContext) where TService : class
        {
            return GetServiceInstanceCore<TService>(RpcObjectId.Empty, syncContext);
        }

        /// <summary>
        /// Disconnects this connection and cleans up any used resources.
        /// </summary>
        /// <returns></returns>
        public abstract Task ShutdownAsync();


        protected abstract IRpcSerializer CreateDefaultSerializer();

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Shut down connection in case it's still connected, but let's 
                // not wait for it to finish (to avoid dead-locks).
                // TODO: Implement IAsyncDisposable
                this.ShutdownAsync().Forget();
            }
        }


        private TService GetServiceInstanceCore<TService>(RpcObjectId refObjectId, SynchronizationContext? syncContext) where TService : class
        {
            return GetServiceInstanceCore<TService>(refObjectId, default, syncContext);
        }

        private TService GetServiceInstanceCore<TService>(
            RpcObjectId refObjectId,
            IReadOnlyCollection<string>? implementedServices,
            SynchronizationContext? syncContext) where TService : class
        {
            lock (this.SyncRoot)
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
                = this.proxyGenerator.GenerateObjectProxyFactory<TService>(implementedServices);

            lock (this.SyncRoot)
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
