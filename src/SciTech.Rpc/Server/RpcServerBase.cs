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

using Microsoft.Extensions.Logging;
using SciTech.Collections;
using SciTech.Collections.Immutable;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Logging;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server.Internal;
using System;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;

namespace SciTech.Rpc.Server
{
    public abstract class RpcServerBase : IRpcServerCore
    {

        private volatile IRpcSerializer? serializer;

        protected RpcServerBase(RpcServicePublisher servicePublisher, IRpcServerOptions? options, ILogger<RpcServerBase>? logger) :
            this(servicePublisher ?? throw new ArgumentNullException(nameof(servicePublisher)),
                servicePublisher,
                servicePublisher.DefinitionsProvider,
                options,
                logger)
        {
        }

        protected RpcServerBase(RpcServerId serverId, IRpcServiceDefinitionsProvider definitionsProvider, IRpcServerOptions? options, ILogger<RpcServerBase>? logger) :
            this(new RpcServicePublisher(definitionsProvider, serverId), options, logger)
        {
        }

        /// <summary>
        /// Only intended for testing.
        /// </summary>
        /// <param name="servicePublisher"></param>
        /// <param name="serviceActivator"></param>
        /// <param name="definitionsProvider"></param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected RpcServerBase(
            IRpcServicePublisher servicePublisher, IRpcServiceActivator serviceActivator,
            IRpcServiceDefinitionsProvider definitionsProvider, IRpcServerOptions? options,
            ILogger<RpcServerBase>? logger)
        {
            this.ServicePublisher = servicePublisher ?? throw new ArgumentNullException(nameof(servicePublisher));
            this.ServiceActivator = serviceActivator ?? throw new ArgumentNullException(nameof(serviceActivator));
            this.ServiceDefinitionsProvider = definitionsProvider ?? throw new ArgumentNullException(nameof(definitionsProvider));
            this.Logger = logger ?? RpcLogger.CreateLogger<RpcServerBase>();

            if (options != null)
            {
                this.serializer = options.Serializer;
                this.AllowDiscovery = options.AllowDiscovery ?? true;
                this.AllowAutoPublish = options.AllowAutoPublish ?? false;

                this.CallInterceptors = options.Interceptors.ToImmutableArrayList();
                this.ExceptionConverters = options.ExceptionConverters.ToImmutableArrayList();
            }

            if (this.ExceptionConverters.Count > 0)
            {
                this.CustomFaultHandler = new RpcServerFaultHandler(null, this.ExceptionConverters, null);
            } else
            {
                this.CustomFaultHandler = RpcServerFaultHandler.Default;
            }
        }

        protected ILogger<RpcServerBase> Logger { get; }


        public bool AllowAutoPublish { get; }

        public bool AllowDiscovery { get; } = true;

        /// <inheritdoc/>
        public RpcServerId ServerId => this.ServicePublisher.ServerId;

        public ImmutableArrayList<RpcServerCallInterceptor> CallInterceptors { get; } = ImmutableArrayList<RpcServerCallInterceptor>.Empty;

        public RpcServerFaultHandler? CustomFaultHandler { get; private set; } = RpcServerFaultHandler.Default;

        public ImmutableArrayList<IRpcServerExceptionConverter> ExceptionConverters { get; } = ImmutableArrayList<IRpcServerExceptionConverter>.Empty;

        public bool IsDisposed { get; private set; }

        public IRpcSerializer Serializer
        {
            get
            {
                if (this.serializer == null)
                {
                    this.serializer = this.CreateDefaultSerializer();
                }

                return this.serializer;
            }
        }

        public IRpcServiceDefinitionsProvider ServiceDefinitionsProvider { get; private set; }

        public IRpcServiceActivator ServiceActivator { get; }

        public IRpcServicePublisher ServicePublisher { get; }

        protected virtual IServiceProvider? ServiceProvider => null;

        protected object SyncRoot { get; } = new object();

        IServiceProvider? IRpcServerCore.ServiceProvider => this.ServiceProvider;


        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing).
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        void IRpcServerCore.HandleCallException(Exception exception, IRpcSerializer? serializer)
        {
            this.HandleCallException(exception, serializer);
        }

        protected virtual void HandleCallException(Exception exception, IRpcSerializer? serializer) { }

        protected virtual void CheckCanStart()
        {
        }

        protected abstract IRpcSerializer CreateDefaultSerializer();

        protected virtual void Dispose(bool disposing)
        {
            if (!this.IsDisposed)
            {
                this.IsDisposed = true;
            }
        }


        protected RpcServicesQueryResponse QueryServices(RpcObjectId objectId)
        {
            var servicesList = this.ServiceActivator.GetPublishedServices(objectId);
            if (!servicesList.IsDefaultOrEmpty)
            {
                return new RpcServicesQueryResponse { ImplementedServices = servicesList.ToArray() };
            }

            throw new RpcServiceUnavailableException($"Service object '{objectId}' not published.");
        }
    }
}
