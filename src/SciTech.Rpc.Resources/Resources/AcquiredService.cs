using SciTech.ComponentModel;
using SciTech.Rpc;
using SciTech.Rpc.Client;
using SciTech.Rpc.Server;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Resources
{
    /// <summary>
    /// Represents an acquired remote service. The lifetime of an acquired service is controlled by the client that requests it.
    /// <para>
    /// Once the client receives the <see cref="AcquiredService{TService}"/> it should call <see cref="TakeOwnershipAsync(IRpcChannel, bool)"/> to verify to the host 
    /// that the client has received the service and to retrieve the service proxy. After <see cref="TakeOwnershipAsync(IRpcChannel, bool)"/>
    /// has been called, the service proxy can be retrieved using the <see cref="Value"/> property.
    /// </para>
    /// <para>
    /// Calling TakeOwnership makes sure that the resource is kept alive on the server, until <see cref="Release"/> is called or the <see cref="AcquiredService{TService}"/>
    /// is finalized. An acquired service can also be transferred to another process. In this case the target process should call TakeOwnership to take over the ownership.
    /// </para>
    /// </summary>
    /// <threadsafety static="true" instance="true"/>

    [DataContract]
    [Serializable]
    public class AcquiredService<TService> where TService : class 
    {
        [DataMember(Order =1)]
        [JsonInclude]
        private AcquiredRemoteResource? serviceResource;

        [DataMember(Order = 2)]
        [JsonInclude]
        private RpcObjectRef<TService>? serviceRef;

        [NonSerialized]
        private TService? service;

        public AcquiredService()
        {
        }

        public AcquiredService(AcquiredRemoteResource serviceResource, RpcObjectRef<TService> serviceRef)
        {
            this.serviceResource = serviceResource;
            this.serviceRef = serviceRef;
        }

        [JsonIgnore]
        public TService? Value => this.service;

        public async Task<TService?> TakeOwnershipAsync(IRpcChannel channel, SynchronizationContext? synchronizationContext)
        {
            var serviceResource = this.serviceResource;
            var serviceRef = this.serviceRef;
            if (serviceResource == null || serviceRef == null) return null;

            if (await serviceResource.TakeOwnershipAsync(channel).ContextFree())
            {
                if (this.service == null)
                {
                    this.service = channel.GetServiceInstance(serviceRef, synchronizationContext);
                }
            }
            else
            {
                this.service = null;
            }

            return this.service;
        }

        public TService? TakeOwnership(IRpcChannel channel, SynchronizationContext? synchronizationContext)
            => this.TakeOwnershipAsync(channel, synchronizationContext).AwaiterResult();

        public Task<TService?> TakeOwnershipAsync(IRpcChannel channel, bool useSyncContext = true)
            => this.TakeOwnershipAsync(channel, useSyncContext ? SynchronizationContext.Current : null);

        public TService? TakeOwnership(IRpcChannel channel, bool useSyncContext = true)
            => this.TakeOwnershipAsync(channel, useSyncContext).AwaiterResult();


        public Task ReleaseAsync()
        {
            this.service = null;
            var serviceResource = this.serviceResource;
            this.serviceResource = null;
            if( serviceResource != null )
            {
                return serviceResource.ReleaseAsync();
            }

            return Task.CompletedTask;            
        }
        
        public void Release()
        {
            this.service = null;
            var serviceResource = this.serviceResource;
            this.serviceResource = null;
            if (serviceResource != null)
            {
                serviceResource.Release();
            }
        }
    }

    public class RemoteClientService<TService> : RemoteResourceBase 
        where TService : class
    {
        IOwned<RpcObjectRef<TService>>? publishedService;

        bool hasBeenAcquired;
        
        public event EventHandler? AcquiredChanged;

        public RemoteClientService(IOwned<RpcObjectRef<TService>> publishedService, RemoteResourceLifetimeManager lifetimeManager ) : base( lifetimeManager, null )
        {
            this.publishedService = publishedService;
        }

        protected internal override void OnAcquiredResourceReleased(AcquiredResourceEventArgs e)
        {
            base.OnAcquiredResourceReleased(e);

            this.publishedService?.Dispose();
            this.publishedService = null;
        }

        protected override bool KeepAliveWhenAcquired => true;

        protected override bool CanAcquireSynchronized() => this.publishedService?.Value != null && !this.hasBeenAcquired;

        protected override void OnAcquiredChanged()
        {
            base.OnAcquiredChanged();
            this.AcquiredChanged?.Invoke(this, EventArgs.Empty);
        }

        public AcquiredService<TService>? Acquire()
        {
            AcquiredRemoteResource? acquiredResource;
            bool acquiredChanged;

            RpcObjectRef<TService>? serviceRef = null;
            lock (this.SyncRoot)
            {
                serviceRef = this.publishedService?.Value;

                (acquiredResource, acquiredChanged) = this.AcquireRemoteResourceCoreSynchronized();
                if (acquiredChanged)
                {
                    this.hasBeenAcquired = true;
                }
            }

            if (acquiredChanged)
            {
                this.OnAcquiredChanged();
            }

            if (acquiredResource != null && serviceRef != null)
            {
                return new AcquiredService<TService>(acquiredResource, serviceRef);
            } else
            {
                acquiredResource?.Release();
            }

            return null;
        }
    }

    public static class RpcPublisherExtensions
    {
        public static AcquiredService<TService> PublishClientService<TService>(this IRpcServicePublisher publisher, TService serviceImpl, RemoteResourceLifetimeManager resourceLifetimeManager)
            where TService : class
        {
            var publishedService = publisher.PublishInstance(serviceImpl, true);

            var serviceResource = new RemoteClientService<TService>(publishedService, resourceLifetimeManager );
            return serviceResource.Acquire()!;
        }

        public static AcquiredService<TService> PublishClientService<TService>(this IRpcServer server, TService serviceImpl, RemoteResourceLifetimeManager resourceLifetimeManager)
            where TService : class
        {
            if (server is null) throw new ArgumentNullException(nameof(server));

            return PublishClientService(server.ServicePublisher, serviceImpl, resourceLifetimeManager);
        }
    }
}
