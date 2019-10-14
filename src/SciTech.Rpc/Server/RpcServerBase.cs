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
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server.Internal;
using SciTech.Threading;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace SciTech.Rpc.Server
{
    public abstract class RpcServerBase : IRpcServerImpl
    {
        private volatile IRpcSerializer? serializer;

        protected RpcServerBase(RpcServicePublisher servicePublisher, RpcServerOptions? options) :
            this(servicePublisher ?? throw new ArgumentNullException(nameof(servicePublisher)),
                servicePublisher,
                servicePublisher.DefinitionsProvider,
                options)
        {
        }
        //private HashSet<string> registeredServices;

        protected RpcServerBase(RpcServerId serverId, IRpcServiceDefinitionsProvider definitionsProvider, RpcServerOptions? options) :
            this(new RpcServicePublisher(definitionsProvider, serverId), options)
        {
        }

        /// <summary>
        /// Only intended for testing.
        /// </summary>
        /// <param name="servicePublisher"></param>
        /// <param name="serviceImplProvider"></param>
        /// <param name="definitionsProvider"></param>
        protected RpcServerBase(
            IRpcServicePublisher servicePublisher, IRpcServiceActivator serviceImplProvider,
            IRpcServiceDefinitionsProvider definitionsProvider, RpcServerOptions? options)
        {
            this.ServicePublisher = servicePublisher ?? throw new ArgumentNullException(nameof(servicePublisher));
            this.ServiceImplProvider = serviceImplProvider ?? throw new ArgumentNullException(nameof(serviceImplProvider));
            this.ServiceDefinitionsProvider = definitionsProvider ?? throw new ArgumentNullException(nameof(definitionsProvider));

            this.ExceptionConverters = this.ServiceDefinitionsProvider.ExceptionConverters;
            this.CallInterceptors = this.ServiceDefinitionsProvider.CallInterceptors;
            this.serializer = options?.Serializer ?? this.ServiceDefinitionsProvider.Options.Serializer;

            if (options != null)
            {
                this.AllowAutoPublish = options.AllowAutoPublish ?? false;

                if (options.Interceptors != null)
                {
                    this.CallInterceptors = this.CallInterceptors.AddRange(options.Interceptors);
                }

                if (options.ExceptionConverters != null)
                {
                    this.ExceptionConverters = this.ExceptionConverters.AddRange(options.ExceptionConverters);
                }
            }

            if (this.ExceptionConverters.Length > 0)
            {
                this.CustomFaultHandler = new RpcServerFaultHandler(this.ExceptionConverters);
            }
        }

        protected enum ServerState
        {
            Initializing,
            Starting,
            Started,
            Stopping,
            Stopped,
            Failed
        }

        public bool AllowAutoPublish { get; set; }

        public ImmutableArray<RpcServerCallInterceptor> CallInterceptors { get; }

        public RpcServerFaultHandler? CustomFaultHandler { get; private set; }

        public ImmutableArray<IRpcServerExceptionConverter> ExceptionConverters { get; private set; }

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

        public IRpcServiceActivator ServiceImplProvider { get; }

        public IRpcServicePublisher ServicePublisher { get; }

        protected virtual IServiceProvider? ServiceProvider => null;

        protected ServerState state { get; private set; }

        protected object syncRoot { get; } = new object();

        IServiceProvider? IRpcServerImpl.ServiceProvider => this.ServiceProvider;

        public abstract void AddEndPoint(IRpcServerEndPoint endPoint);

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing).
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        public RpcObjectRef<TService>? GetPublishedServiceInstance<TService>(TService serviceInstance) where TService : class
        {
            return this.ServicePublisher.GetPublishedInstance(serviceInstance);
        }

        public ScopedObject<RpcObjectRef<TService>> PublishServiceInstance<TService>(TService serviceInstance, bool takeOwnership = false) where TService : class
        {
            return this.ServicePublisher.PublishInstance(serviceInstance, takeOwnership);
        }

        public ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TServiceImpl, TService>() where TService : class where TServiceImpl : class, TService
        {
            return this.ServicePublisher.PublishSingleton<TServiceImpl, TService>();
        }

        public ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TService>(Func<TService> singletonFactory) where TService : class
            => this.ServicePublisher.PublishSingleton<TService>(singletonFactory);

        public ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TService>(Func<IServiceProvider, TService> singletonFactory) where TService : class
            => this.ServicePublisher.PublishSingleton(singletonFactory);

        public ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TService>(TService singletonService, bool takeOwnership = false) where TService : class
        {
            return this.ServicePublisher.PublishSingleton(singletonService, takeOwnership);
        }

        public async Task ShutdownAsync()
        {
            bool waitForState = false;

            lock (this.syncRoot)
            {
                switch (this.state)
                {
                    case ServerState.Initializing:
                        this.state = ServerState.Stopped;
                        return;
                    case ServerState.Failed:
                    case ServerState.Stopped:
                        return;
                    case ServerState.Stopping:
                    case ServerState.Starting:
                        waitForState = true;
                        break;
                    default:
                        this.state = ServerState.Stopping;
                        break;
                }
            }

            if (waitForState)
            {
                throw new NotImplementedException();
            }

            try
            {
                await this.ShutdownCoreAsync().ContextFree();

                lock (this.syncRoot)
                {
                    this.state = ServerState.Stopped;
                }
            }
            finally
            {
                lock (this.syncRoot)
                {
                    if (this.state == ServerState.Stopping)
                    {
                        this.state = ServerState.Failed;
                    }
                }
            }
        }

        /// <summary>
        /// Starts this RPC server. Will generate service stubs and start listening on the configured endpoints.
        /// </summary>
        public void Start()
        {
            this.CheckCanStart();

            lock (this.syncRoot)
            {
                if (this.state != ServerState.Initializing)
                {
                    throw new InvalidOperationException("Server can only be started once.");
                }

                this.state = ServerState.Starting;
            }

            try
            {
                this.BuildServiceStubs();
                this.StartCore();
                lock (this.syncRoot)
                {
                    this.state = ServerState.Started;
                }
            }
            finally
            {
                lock (this.syncRoot)
                {
                    if (this.state == ServerState.Starting)
                    {
                        this.state = ServerState.Failed;
                    }
                }
            }
        }

        public void UnpublishInstance(RpcObjectId serviceInstanceId)
        {
            this.ServicePublisher.UnpublishInstance(serviceInstanceId);
        }

        public void UnpublishSingleton<TService>() where TService : class
        {
            this.ServicePublisher.UnpublishSingleton<TService>();
        }

        protected abstract void BuildServiceStub(Type serviceType);

        /// <summary>
        /// 
        /// </summary>
        protected virtual void BuildServiceStubs()
        {
            foreach (Type serviceType in this.ServiceDefinitionsProvider.GetRegisteredServiceTypes())
            {
                this.BuildServiceStub(serviceType);
            }
        }

        protected virtual void CheckCanStart()
        {
            this.CheckConnectionInfo();
        }

        protected void CheckIsInitializing()
        {
            if (this.state != ServerState.Initializing)
            {
                throw new InvalidOperationException("");
            }
        }

        protected abstract IRpcSerializer CreateDefaultSerializer();

        protected virtual void Dispose(bool disposing)
        {
            if (!this.IsDisposed)
            {
                this.IsDisposed = true;
            }
        }
        //protected void Init(HashSet<string> registeredServices)
        //{
        //    this.registeredServices = registeredServices ?? throw new ArgumentNullException(nameof(registeredServices));
        //}

        protected RpcServicesQueryResponse QueryServices(RpcObjectId objectId)
        {
            var servicesList = this.ServiceImplProvider.GetPublishedServices(objectId);
            if (!servicesList.IsDefaultOrEmpty)
            {
                return new RpcServicesQueryResponse { ImplementedServices = servicesList.ToArray() };
            }

            throw new RpcServiceUnavailableException($"Service object '{objectId}' not published.");
        }

        protected virtual Task ShutdownCoreAsync()
        {
            return Task.CompletedTask;
        }

        protected abstract void StartCore();


        private void CheckConnectionInfo()
        {
            //lock (this.syncRoot)
            //{
            //    if (this.connectionInfo == null)
            //    {
            //        throw new InvalidOperationException("ConnectionInfo not initialized.");
            //    }
            //}
        }
    }
}
