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
using SciTech.Rpc.Server.Internal;
using SciTech.Threading;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SciTech.Rpc.Server
{
    public abstract class RpcServerHostBase : RpcServerBase, IRpcServerHost
    {
        protected RpcServerHostBase(RpcServicePublisher servicePublisher, 
            IRpcServerOptions? options, ILogger<RpcServerHostBase>? logger=null) :
            this(servicePublisher ?? throw new ArgumentNullException(nameof(servicePublisher)),
                servicePublisher,
                servicePublisher.DefinitionsProvider,
                options, logger )
        {
        }

        protected RpcServerHostBase(RpcServerId serverId, IRpcServiceDefinitionsProvider definitionsProvider, IRpcServerOptions? options, ILogger<RpcServerHostBase>? logger = null) :
            this(new RpcServicePublisher(definitionsProvider, serverId), options, logger)
        {
        }

        /// <summary>
        /// Only intended for testing.
        /// </summary>
        /// <param name="servicePublisher"></param>
        /// <param name="serviceImplProvider"></param>
        /// <param name="definitionsProvider"></param>
        protected RpcServerHostBase(
            IRpcServicePublisher servicePublisher, IRpcServiceActivator serviceImplProvider,
            IRpcServiceDefinitionsProvider definitionsProvider, IRpcServerOptions? options,
            ILogger<RpcServerHostBase>? logger = null )
            : base(servicePublisher, serviceImplProvider, definitionsProvider, options, logger)
        {

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

        protected ServerState State { get; private set; }

        public abstract void AddEndPoint(IRpcServerEndPoint endPoint);

        public async Task ShutdownAsync()
        {
            bool waitForState = false;

            lock (this.SyncRoot)
            {
                switch (this.State)
                {
                    case ServerState.Initializing:
                        this.State = ServerState.Stopped;
                        return;
                    case ServerState.Failed:
                    case ServerState.Stopped:
                        return;
                    case ServerState.Stopping:
                    case ServerState.Starting:
                        waitForState = true;
                        break;
                    default:
                        this.State = ServerState.Stopping;
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

                lock (this.SyncRoot)
                {
                    this.State = ServerState.Stopped;
                }
            }
            finally
            {
                lock (this.SyncRoot)
                {
                    if (this.State == ServerState.Stopping)
                    {
                        this.State = ServerState.Failed;
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

            lock (this.SyncRoot)
            {
                if (this.State != ServerState.Initializing)
                {
                    throw new InvalidOperationException("Server can only be started once.");
                }

                this.State = ServerState.Starting;
            }

            try
            {
                this.BuildServiceStubs();
                this.StartCore();
                lock (this.SyncRoot)
                {
                    this.State = ServerState.Started;
                }
            }
            finally
            {
                lock (this.SyncRoot)
                {
                    if (this.State == ServerState.Starting)
                    {
                        this.State = ServerState.Failed;
                    }
                }
            }
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

        protected void CheckIsInitializing()
        {
            if (this.State != ServerState.Initializing)
            {
                throw new InvalidOperationException("");
            }
        }

        protected virtual Task ShutdownCoreAsync()
        {
            return Task.CompletedTask;
        }

        protected abstract void StartCore();
    }
}
