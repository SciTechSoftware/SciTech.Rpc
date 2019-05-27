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
using SciTech.Rpc.Internal;
using SciTech.Threading;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace SciTech.Rpc.Server
{
    public abstract class RpcServerBase : IRpcServerImpl
    {
        private ImmutableArray<RpcServerCallInterceptor>.Builder? callInterceptorsBuilder = ImmutableArray.CreateBuilder<RpcServerCallInterceptor>();
        //private HashSet<string> registeredServices;

        protected RpcServerBase(RpcServerId serverId, IRpcServiceDefinitionsProvider definitionsProvider, RpcServiceOptions? options) : 
            this(new RpcServicePublisher(definitionsProvider, serverId), options)
        {
            var servicePublisher = new RpcServicePublisher(definitionsProvider, serverId );
            this.ServicePublisher = servicePublisher;
            this.ServiceImplProvider = servicePublisher;
            this.ServiceDefinitionsProvider = definitionsProvider;
        }

        protected RpcServerBase(RpcServicePublisher servicePublisher, RpcServiceOptions? options) :
            this( servicePublisher, servicePublisher, servicePublisher.DefinitionsProvider, options)
        {
        }

        /// <summary>
        /// Only intended for testing.
        /// </summary>
        /// <param name="servicePublisher"></param>
        /// <param name="serviceImplProvider"></param>
        /// <param name="definitionsProvider"></param>
        protected RpcServerBase(IRpcServicePublisher servicePublisher, IRpcServiceActivator serviceImplProvider, IRpcServiceDefinitionsProvider definitionsProvider, RpcServiceOptions? options)
        {
            this.ServicePublisher = servicePublisher;
            this.ServiceImplProvider = serviceImplProvider;
            this.ServiceDefinitionsProvider = definitionsProvider;
            this.InitOptions(options);
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

        public ImmutableArray<RpcServerCallInterceptor> CallInterceptors { get; private set; }

        public bool IsDisposed { get; private set; }

        public IRpcServiceActivator ServiceImplProvider { get; }

        public IRpcServicePublisher ServicePublisher { get; }
        
        public IRpcServiceDefinitionsProvider ServiceDefinitionsProvider { get; private set; }

        protected ServerState state { get; private set; }

        public bool AllowAutoPublish { get; set; }

        protected virtual IServiceProvider? ServiceProvider => null;

        IServiceProvider? IRpcServerImpl.ServiceProvider => this.ServiceProvider;

        protected object syncRoot { get; } = new object();

        public void AddCallInterceptor(RpcServerCallInterceptor callInterceptor)
        {
            if (callInterceptor == null)
            {
                throw new ArgumentNullException(nameof(callInterceptor));
            }

            lock (this.syncRoot)
            {
                if (this.state != ServerState.Initializing)
                {
                    throw new InvalidOperationException("Call interceptor cannot be added after server has been started.");
                }

                this.callInterceptorsBuilder!.Add(callInterceptor);
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
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

        public ScopedObject<RpcSingletonRef<TService>> PublishSingleton<TService>(TService singletonService) where TService : class
        {
            return this.ServicePublisher.PublishSingleton(singletonService);
        }

        public void PublishSingleton<TService>(Func<TService> singletonFactory) where TService : class
        {
            throw new NotImplementedException();
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
                this.CallInterceptors = this.callInterceptorsBuilder!.ToImmutable();
                this.callInterceptorsBuilder = null;
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

        protected abstract void AddEndPoint(IRpcServerEndPoint endPoint);

        protected abstract void BuildServiceStub(Type serviceType);
        //protected virtual void ValidateConnectionInfo(RpcServerConnectionInfo value)
        //{
        //}

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
            if (servicesList?.Count > 0)
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

        void IRpcServer.AddEndPoint(IRpcServerEndPoint endPoint)
        {
            this.AddEndPoint(endPoint);
        }

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
        private void InitOptions(RpcServiceOptions? options)
        {
            if (options != null)
            {
                this.AllowAutoPublish = options.AllowAutoPublish;

                if (options.Interceptors != null)
                {
                    foreach (var interceptor in options.Interceptors)
                    {
                        this.AddCallInterceptor(interceptor);
                    }
                }
            }
        }

        //TService IServiceImplProvider.GetServiceImpl<TService>(RpcObjectId id)
        //{
        //    return this.ServicePublisher.GetServiceImpl<TService>(id);
        //    //var key = new ServiceImplKey(id, typeof(TService));
        //    //lock (this.syncRoot)
        //    //{
        //    //    if (this.idToServiceImpl.TryGetValue(key, out object serviceImpl) && serviceImpl is TService service)
        //    //    {
        //    //        return service;
        //    //    }
        //    //}
        //    //return null;
        //}
        //TService IServiceImplProvider.GetSingletonServiceImpl<TService>()
        //{
        //    return this.ServicePublisher.GetSingletonServiceImpl<TService>();
        //}
    }
}
