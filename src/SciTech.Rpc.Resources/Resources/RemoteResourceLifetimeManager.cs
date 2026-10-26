#region Copyright notice and license
// Copyright (c) SciTech Software AB and contributors.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using SciTech.ComponentModel;
using SciTech.Rpc.Server;
using System;

namespace SciTech.Rpc.Resources
{
    /// <summary>
    /// This class is used internally for lifetime management of an acquired RemoteResource.
    /// </summary>
    public sealed class RemoteResourceLifetimeManager : IDisposable
    {
        private static readonly TimeSpan TimerInterval = TimeSpan.FromSeconds(10);

        private readonly object syncRoot = new object();

        private readonly Dictionary<Guid, object> trackedResources = new Dictionary<Guid, object>();

        private IOwned<RpcObjectRef<IRemoteResourceLifetimeService>>? scopedServiceRef;

        private Timer? timer;

        public RemoteResourceLifetimeManager()
        {
        }

        internal RpcObjectRef<IRemoteResourceLifetimeService>? ServiceRef => this.scopedServiceRef?.Value;

        public void Dispose()
        {
            this.timer?.Dispose();
            this.timer = null;

            this.scopedServiceRef?.Dispose();
            this.scopedServiceRef = null;
        }

        public void Publish(IRpcServicePublisher publisher)
        {
            if (this.scopedServiceRef != null)
            {
                throw new InvalidOperationException("RemoteResourceLifetimeManager already published.");
            }

            this.scopedServiceRef = publisher.PublishInstance<IRemoteResourceLifetimeService>(new LifetimeService(this));
        }

        internal void Add(RemoteResourceBase resource, bool keepAlive)
        {
            lock (this.syncRoot)
            {
                this.trackedResources.Add(resource.ResourceId, keepAlive ? resource : new WeakReference<RemoteResourceBase>(resource));
                if (this.trackedResources.Count == 1)
                {
                    this.timer = new Timer(this.timer_TimerCallback, null, TimerInterval, TimerInterval);
                }
            }
        }

        internal void Remove(RemoteResourceBase resource)
        {
            lock (this.syncRoot)
            {
                this.trackedResources.Remove(resource.ResourceId);

                if (this.trackedResources.Count == 0)
                {
                    this.timer?.Dispose();
                    this.timer = null;
                }
            }
        }

        private RemoteResourceBase? GetTrackedResource(Guid resourceId)
        {
            lock (this.syncRoot)
            {
                return GetTrackedResourceSynchronized(resourceId);
            }
        }

        private RemoteResourceBase? GetTrackedResourceSynchronized(Guid resourceId)
        {
            RemoteResourceBase? remoteResource = null;
            if (this.trackedResources.TryGetValue(resourceId, out var oResource))
            {
                if (oResource is WeakReference<RemoteResourceBase> wrResource)
                {
                    wrResource.TryGetTarget(out remoteResource);
                }
                else
                {
                    remoteResource = oResource as RemoteResourceBase;
                }
            }

            return remoteResource;
        }

        private void timer_TimerCallback(object? state)
        {
            List<RemoteResourceBase> liveResources;
            lock (this.syncRoot)
            {
                liveResources = new List<RemoteResourceBase>(this.trackedResources.Count);
                foreach (var pair in this.trackedResources.ToArray())
                {
                    RemoteResourceBase? remoteResource;
                    if (pair.Value is WeakReference<RemoteResourceBase> wrResource)
                    {
                        wrResource.TryGetTarget(out remoteResource);
                    }
                    else
                    {
                        remoteResource = pair.Value as RemoteResourceBase;
                    }

                    if (remoteResource != null)
                    {
                        liveResources.Add(remoteResource);
                    }
                    else
                    {
                        this.trackedResources.Remove(pair.Key);
                    }
                }
            }

            foreach (var liveResource in liveResources)
            {
                liveResource.ReleaseLeftOvers();
            }
        }

        internal class LifetimeService : IRemoteResourceLifetimeService
        {
            private readonly RemoteResourceLifetimeManager owner;

            internal LifetimeService(RemoteResourceLifetimeManager owner)
            {
                this.owner = owner;
            }

            public bool KeepAlive(Guid resourceId, int acquiredResourceId, int ownerId)
            {
                RemoteResourceBase? remoteResource = this.owner.GetTrackedResource(resourceId);
                return remoteResource?.KeepAlive(acquiredResourceId, ownerId) ?? false;
            }

            public void Release(Guid resourceId, int acquiredResourceId, int ownerId)
            {
                RemoteResourceBase? remoteResource = this.owner.GetTrackedResource(resourceId);
                remoteResource?.ReleaseAcquiredResource(acquiredResourceId, ownerId);
            }

            public int TakeOwnership(Guid resourceId, int acquiredResourceId)
            {
                RemoteResourceBase? remoteResource = this.owner.GetTrackedResource(resourceId);
                return remoteResource?.TakeOwnership(acquiredResourceId) ?? RemoteResourceBase.InvalidOwnerId;
            }
        }
    }
}