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

using SciTech.Diagnostics;
using SciTech.Rpc.Client;

namespace SciTech.Rpc.Resources
{
    [RpcService]
    public interface IRemoteResourceLifetimeService
    {
        bool KeepAlive(Guid resourceId, int acquiredResourceId, int ownerId);

        void Release(Guid resourceId, int acquiredResourceId, int ownerId);

        int TakeOwnership(Guid resourceId, int acquiredResourceId);
    }

    [RpcService(ServerDefinitionType = typeof(IRemoteResourceLifetimeService))]
    public interface IRemoteResourceLifetimeServiceClient : IRemoteResourceLifetimeService, IRpcProxy
    {
        Task<bool> KeepAliveAsync(Guid resourceId, int acquiredResourceId, int ownerId);

        Task ReleaseAsync(Guid resourceId, int acquiredResourceId, int ownerId);

        Task<int> TakeOwnershipAsync(Guid resourceId, int acquiredResourceId);
    }

    public sealed class AcquiredResourceEventArgs : EventArgs
    {
        internal AcquiredResourceEventArgs(AcquiredResourceId acquiredResourceId)
        {
            this.AcquiredResourceId = acquiredResourceId;
        }

        public AcquiredResourceId AcquiredResourceId { get; }
    }

    /// <summary>
    ///
    /// </summary>
    public class RemoteResource : RemoteResourceBase
    {
        private int maxAcquireCount;

        public RemoteResource(RemoteResourceLifetimeManager lifetimeManager) : this(lifetimeManager, int.MaxValue, null)
        {
        }

        public RemoteResource(RemoteResourceLifetimeManager lifetimeManager, int maxAcquireCount) : this(lifetimeManager, maxAcquireCount, null)
        {
        }

        internal RemoteResource(RemoteResourceLifetimeManager lifetimeManager, int maxAcquireCount, object? syncRoot) : base(lifetimeManager, syncRoot)
        {
            this.maxAcquireCount = maxAcquireCount;
        }

        public event EventHandler? AcquiredChanged;

        public AcquiredRemoteResource? Acquire()
        {
            AcquiredRemoteResource? acquiredResource;
            bool acquiredChanged;

            lock (this.SyncRoot)
            {
                (acquiredResource, acquiredChanged) = this.AcquireRemoteResourceCoreSynchronized();
            }

            if (acquiredChanged)
            {
                this.OnAcquiredChanged();
            }

            return acquiredResource;
        }

        protected override bool CanAcquireSynchronized()
        {
            return this.AcquiredResourceCount < this.maxAcquireCount;
        }

        protected override void OnAcquiredChanged()
        {
            base.OnAcquiredChanged();
            this.AcquiredChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public abstract class RemoteResourceBase
    {
        internal const int InvalidAcquiredResourceId = 0;

        internal const int InvalidOwnerId = 0;

        private static readonly TimeSpan ResourceTimeOut = TimeSpan.FromSeconds(70);

        private readonly RemoteResourceLifetimeManager lifetimeManager;
        private List<AcquiredResourceData> acquiredResources = new List<AcquiredResourceData>();

        private bool isActive;

        private int lastAcquiredResourceId = InvalidAcquiredResourceId;

        private int lastOwnerId = InvalidOwnerId;
        private object syncRoot;

        protected RemoteResourceBase(RemoteResourceLifetimeManager lifetimeManager, object? syncRoot)
        {
            this.lifetimeManager = lifetimeManager ?? throw new ArgumentNullException(nameof(lifetimeManager));
            this.ResourceId = Guid.NewGuid();
            this.syncRoot = syncRoot ?? new object();
        }

        public event EventHandler<AcquiredResourceEventArgs>? AcquiredResourceReleased;

        public bool IsAcquired
        {
            get
            {
                lock (this.syncRoot)
                {
                    return this.acquiredResources.Count > 0;
                }
            }
        }

        /// <summary>
        /// Gets the SyncRoot of this RemoteResource.
        /// </summary>
        public object SyncRoot
        {
            get { return this.syncRoot; }
        }

        internal bool IsAcquiredSynchronized => this.acquiredResources.Count > 0;
        internal Guid ResourceId { get; }

        protected int AcquiredResourceCount
        {
            get
            {
                lock (this.syncRoot)
                {
                    return this.acquiredResources.Count;
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the resource should be kept alive as long
        /// as it is acquired. By default this is <c>false</c> and the resource can be GCed
        /// if no local reference to the resource exists.
        /// </summary>
        protected virtual bool KeepAliveWhenAcquired => false;

        public void ReleaseAll()
        {
            List<AcquiredResourceId>? releasedResources = null;
            lock (this.syncRoot)
            {
                if (this.acquiredResources.Count > 0)
                {
                    releasedResources = this.acquiredResources.Select(r => new AcquiredResourceId(this.ResourceId, r.acquiredResourceId)).ToList();
                }
            }

            if (releasedResources != null)
            {
                foreach (var releasedResourceId in releasedResources)
                {
                    this.OnAcquiredResourceReleased(new AcquiredResourceEventArgs(releasedResourceId));
                }

                this.OnAcquiredChanged();
            }
        }

        internal (AcquiredRemoteResource?, bool) AcquireRemoteResourceCoreSynchronized()
        {
            var serviceRef = this.lifetimeManager.ServiceRef;
            if (serviceRef == null)
            {
                throw new InvalidOperationException("Lifetime manager service must be published before remote resource can be acquired.");
            }

            AcquiredRemoteResource? retVal = null;
            bool acquiredChanged = false;
            if (this.CanAcquireSynchronized())
            {
                acquiredChanged = this.acquiredResources.Count == 0;

                var resData = new AcquiredResourceData(++this.lastAcquiredResourceId, null);
                this.acquiredResources.Add(resData);
                retVal = new AcquiredRemoteResource(serviceRef, this.ResourceId, resData.acquiredResourceId);

                if (acquiredChanged)
                {
                    this.UpdateActiveSynchronized();
                }
            }

            return (retVal, acquiredChanged);
        }

        internal bool KeepAlive(int acquiredResourceId, int ownerId)
        {
            AcquiredResourceData? ard;
            lock (this.syncRoot)
            {
                ard = this.FindResourceData(acquiredResourceId);
                if (ard?.ownerId == ownerId)
                {
                    ard.lastTimeAlive = ProgramTickCount.Now;
                    return true;
                }
            }

            return false;
        }

        internal void ReleaseAcquiredResource(int acquiredResourceId, int ownerId)
        {
            bool acquiredChanged = false;

            List<AcquiredResourceId>? releasedResources = null;
            lock (this.syncRoot)
            {
                AcquiredResourceData? resourceData = this.FindResourceData(acquiredResourceId);
                if (resourceData != null && resourceData.ownerId == ownerId)
                {
                    releasedResources = new();
                    releasedResources.Add(new AcquiredResourceId(this.ResourceId, resourceData.acquiredResourceId));
                    this.acquiredResources.Remove(resourceData);

                    if (this.acquiredResources.Count == 0)
                    {
                        acquiredChanged = true;

                        this.UpdateActiveSynchronized();
                    }
                }
            }

            if (releasedResources != null)
            {
                foreach (var releasedResourceId in releasedResources)
                {
                    this.OnAcquiredResourceReleased(new AcquiredResourceEventArgs(releasedResourceId));
                }
            }

            if (acquiredChanged)
            {
                this.OnAcquiredChanged();
            }
        }

        internal void ReleaseLeftOvers()
        {
            //			bool lockStateChanged = false;
            //			ReaderWriterLockState newLockState;
            //			ArrayList releasedResources = null;
            bool acquiredChanged = false;
            List<AcquiredResourceId>? releasedResources = null;
            lock (this.syncRoot)
            {
                TickCount currTime = ProgramTickCount.Now;

                for (int i = this.acquiredResources.Count; --i >= 0;)
                {
                    AcquiredResourceData resData = this.acquiredResources[i];
                    if (this.ReleaseLeftOver(resData, currTime))
                    {
                        releasedResources ??= new();
                        releasedResources.Add(new AcquiredResourceId(this.ResourceId, resData.acquiredResourceId));
                    }
                }

                if (this.acquiredResources.Count == 0 && releasedResources != null)
                {
                    acquiredChanged = true;
                }
            }

            if (releasedResources != null)
            {
                foreach (var releasedResourceId in releasedResources)
                {
                    this.OnAcquiredResourceReleased(new AcquiredResourceEventArgs(releasedResourceId));
                }
            }

            if (acquiredChanged)
            {
                this.OnAcquiredChanged();
            }
        }

        internal int TakeOwnership(int acquiredResourceId)
        {
            lock (this.syncRoot)
            {
                AcquiredResourceData? resourceData = this.FindResourceData(acquiredResourceId);
                if (resourceData != null)
                {
                    resourceData.ownerId = ++this.lastOwnerId;
                    return resourceData.ownerId;
                }

                return InvalidOwnerId;
            }
        }

        /// <summary>
        ///
        /// </summary>
        protected internal virtual void OnAcquiredResourceReleased(AcquiredResourceEventArgs e)
        {
            this.AcquiredResourceReleased?.Invoke(this, e);
        }

        protected virtual bool CanAcquireSynchronized()
        {
            return true;
        }

        /// <summary>
        /// Called when the IsAcquired state has changed. If derived classes
        /// override this method, they must call base.OnAcquiredChanged.
        /// </summary>
        protected virtual void OnAcquiredChanged()
        {
        }

        private AcquiredResourceData? FindResourceData(int acquiredResourceId)
        {
            for (int i = 0; i < this.acquiredResources.Count; i++)
            {
                AcquiredResourceData resData = this.acquiredResources[i];
                if (resData.acquiredResourceId == acquiredResourceId)
                {
                    return resData;
                }
            }
            return null;
        }

        private bool ReleaseLeftOver(AcquiredResourceData resData, TickCount currTime)
        {
            if (resData != null)
            {
                if (currTime - resData.lastTimeAlive > ResourceTimeOut)
                {
                    this.acquiredResources.Remove(resData);
                    return true;
                }
            }

            return false;
        }

        private void UpdateActiveSynchronized()
        {
            bool shouldBeActive = this.acquiredResources.Count > 0;

            if (shouldBeActive != this.isActive)
            {
                this.isActive = shouldBeActive;
                if (this.isActive)
                {
                    this.lifetimeManager.Add(this, this.KeepAliveWhenAcquired);
                }
                else
                {
                    this.lifetimeManager.Remove(this);
                }
            }
        }

        protected sealed class AcquiredResourceData
        {
            internal int acquiredResourceId;

            internal object? additionalLockData;

            internal TickCount lastTimeAlive;

            internal int ownerId;

            internal AcquiredResourceData(int acquiredResourceId, object? additionalLockData)
            {
                this.acquiredResourceId = acquiredResourceId;
                this.additionalLockData = additionalLockData;
                this.lastTimeAlive = ProgramTickCount.Now;
                this.ownerId = InvalidOwnerId;
            }
        }
    }
}