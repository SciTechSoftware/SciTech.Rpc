using Microsoft.Extensions.Logging;
using SciTech.Diagnostics;
using SciTech.Rpc.Client;
using SciTech.Rpc.Logging;
using SciTech.Threading;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace SciTech.Rpc.Resources
{
    public readonly struct AcquiredResourceId : IEquatable<AcquiredResourceId>
    {
        private readonly int acquiredResourceCookie;
        private readonly Guid resourceId;

        public AcquiredResourceId(Guid resourceId, int acquiredResourceCookie)
        {
            this.resourceId = resourceId;
            this.acquiredResourceCookie = acquiredResourceCookie;
        }

        public static bool operator !=(AcquiredResourceId id1, AcquiredResourceId id2) => !id1.Equals(id2);

        public static bool operator ==(AcquiredResourceId id1, AcquiredResourceId id2) => id1.Equals(id2);

        public bool Equals(AcquiredResourceId other)
        {
            return this.resourceId == other.resourceId && this.acquiredResourceCookie == other.acquiredResourceCookie;
        }

        public override bool Equals(object? obj) => obj is AcquiredResourceId other && this.Equals(other);

        public override int GetHashCode() => this.resourceId.GetHashCode() + this.acquiredResourceCookie.GetHashCode();
    }

    /// <summary>
    /// Represents an acquired remote resource. An instance of this class is returned from a remote service when a resource is requested.
    /// Once the client receives the AcquiredRemoteResource it should call <see cref="TakeOwnershipAsync(IRpcChannel)"/> to verify to the host that the client has received the resource acquisition.
    /// Calling TakeOwnership makes sure that the resource is kept alive on the server, until <see cref="Release"/> is called or the <see cref="AcquiredRemoteResource"/> is finalized.
    /// An acquired resource can also be transferred to another process. In this case the target process should call TakeOwnership to take over the ownership.
    /// </summary>
    /// <threadsafety static="true" instance="true"/>
    [SuppressMessage("Usage", "CA1816:Dispose methods should call SuppressFinalize", Justification = "<Pending>")]
    [DataContract]
    [Serializable]
    // The order of AcquiredRemoteLock is part of the wire format (it was previously declared as ProtoInclude(10)).
    [RpcKnownType(typeof(AcquiredRemoteLock), 10)]
    public class AcquiredRemoteResource : IDisposable
    {
        [NonSerialized]
        private bool isReleased;

        [JsonConstructor]
        internal AcquiredRemoteResource()
        {
        }

        internal AcquiredRemoteResource(RpcObjectRef<IRemoteResourceLifetimeService>? lifetimeServiceRef, Guid resourceId, int acquiredLockId)
        {
            this.ResourceId = resourceId;
            this.LifetimeServiceRef = lifetimeServiceRef;
            this.AcquiredResourceCookie = acquiredLockId;
        }

        ~AcquiredRemoteResource()
        {
            if (!AppDomain.CurrentDomain.IsFinalizingForUnload())
            {
                lock (this.SyncRoot)
                {
                    if (this.OwnerId != RemoteResourceBase.InvalidOwnerId)
                    {
                        ResourcesLog.UnreleasedResourceFinalized(RpcLogger.CreateLogger<AcquiredRemoteResource>(), null);

                        this.OwnerId = RemoteResourceBase.InvalidOwnerId;
                        this.isReleased = true;
                        // TODO: Check this. Has the RemoteResourcesManager weak reference to this AcquiredRemoteResource
                        // been cleared. If not, it may not be removed by PerformCleanup.
                        Task.Run(new Action(AcquiredResourceKeepAliveHelper.PerformCleanup)).Forget();
                    }
                }
            }
        }

        [JsonIgnore]
        public AcquiredResourceId Id => new AcquiredResourceId(this.ResourceId, this.AcquiredResourceCookie);

        [DataMember(Order = 1)]
        [JsonInclude]
        internal int AcquiredResourceCookie { get; private set; }

        [field: NonSerialized]
        internal IRemoteResourceLifetimeServiceClient? LifetimeService { get; private set; }

        [DataMember(Order = 2)]
        [JsonInclude]
        internal RpcObjectRef<IRemoteResourceLifetimeService>? LifetimeServiceRef { get; private set; }

        internal int OwnerId { get; private set; } = RemoteResourceBase.InvalidOwnerId;

        [DataMember(Order = 3)]
        [JsonInclude]
        internal Guid ResourceId { get; private set; }

        private object SyncRoot => this;

        /// <summary>
        /// Begins a background releases of the remote resource.
        /// </summary>
		public void BeginRelease()
        {
            this.ReleaseAsync().Forget();
        }

        public void Release()
        {
            this.ReleaseAsync().AwaiterResult();
        }

        public async Task ReleaseAsync()
        {
            IRemoteResourceLifetimeServiceClient? lifetimeService = null;
            int ownerId = RemoteResourceBase.InvalidOwnerId;

            lock (this.SyncRoot)
            {
                if (!this.isReleased)
                {
                    GC.SuppressFinalize(this);

                    this.isReleased = true;
                    ownerId = this.OwnerId;
                    this.OwnerId = RemoteResourceBase.InvalidOwnerId;
                    lifetimeService = this.LifetimeService;
                    this.LifetimeService = null;
                }
            }

            if (lifetimeService != null && ownerId != RemoteResourceBase.InvalidOwnerId)
            {
                AcquiredResourceKeepAliveHelper.Remove(this);
                try
                {
                    await lifetimeService.ReleaseAsync(this.ResourceId, this.AcquiredResourceCookie, ownerId).ConfigureAwait(false);
                }
                catch (Exception e) when (!e.IsFatal())
                {
                    ResourcesLog.ReleaseFailed(RpcLogger.CreateLogger<AcquiredRemoteResource>(), e);
                }
            }
        }

        /// <inheritdoc cref="TakeOwnershipAsync(IRpcConnectionManager)"/>
        public bool TakeOwnership(IRpcConnectionManager connectionManager)
        {
            return this.TakeOwnershipAsync(connectionManager).AwaiterResult();
        }

        /// <inheritdoc cref="TakeOwnershipAsync(IRpcChannel)"/>
        public bool TakeOwnership(IRpcChannel connection)
        {
            return this.TakeOwnershipAsync(connection).AwaiterResult();
        }

        /// <summary>
        /// Takes ownership of the remote resource. Must be called when the AcquiredRemoteResource has been retrieved from the remote host.
        /// The ownership will be kept alive until <see cref="Release"/> is called.
        /// NOTE! This method should only be called once for an acquired resource. It is thread-safe, but if it's called from multiple threads at the same time there will be a race condition and
        /// there a risk that the resource acquisition will not be correctly kept alive.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Exception">Communication exception if the remote resource host cannot be reached.</exception>
		public async Task<bool> TakeOwnershipAsync(IRpcConnectionManager connectionManager)
        {
            IRemoteResourceLifetimeServiceClient? lifetimeService = null;
            lock (this.SyncRoot)
            {
                if (this.isReleased)
                {
                    throw new InvalidOperationException("Acquired resource has already been released.");
                }

                lifetimeService = this.LifetimeService;
            }

            if (lifetimeService == null)
            {
                var serviceRef = this.LifetimeServiceRef;
                if (serviceRef == null)
                {
                    ResourcesLog.LifetimeServiceRefMissing(RpcLogger.CreateLogger<AcquiredRemoteResource>(), null);
                    return false;
                }
                
                lifetimeService = connectionManager.GetServiceInstance<IRemoteResourceLifetimeServiceClient>(serviceRef, false);
            }

            lock (this.SyncRoot)
            {
                if (this.LifetimeService == null)
                {
                    this.LifetimeService = lifetimeService;
                }
                else
                {
                    lifetimeService = this.LifetimeService;
                }
            }

            var newOwnerId = await lifetimeService.TakeOwnershipAsync(this.ResourceId, this.AcquiredResourceCookie).ConfigureAwait(false);
            if (newOwnerId != RemoteResourceBase.InvalidOwnerId)
            {
                lock (this.SyncRoot)
                {
                    this.InitOwner(newOwnerId);
                }

                AcquiredResourceKeepAliveHelper.Add(this);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Takes ownership of the remote resource. Must be called when the AcquiredRemoteResource has been retrieved from the remote server.
        /// The ownership will be kept alive until <see cref="Release"/> is called.
        /// NOTE! This method should only be called once for an acquired resource. It is thread-safe, but if it's called from multiple threads at the same time there will be a race condition and
        /// there a risk that the resource acquisition will not be correctly kept alive.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Exception">Communication exception if the remote resource host cannot be reached.</exception>
		public async Task<bool> TakeOwnershipAsync(IRpcChannel channel)
        {
            IRemoteResourceLifetimeServiceClient? lifetimeService = null;
            lock (this.SyncRoot)
            {
                if (this.isReleased)
                {
                    throw new InvalidOperationException("Acquired resource has already been released.");
                }

                lifetimeService = this.LifetimeService;
            }

            if (lifetimeService == null)
            {
                var serviceRef = this.LifetimeServiceRef;
                if (serviceRef == null)
                {
                    ResourcesLog.LifetimeServiceRefMissing(RpcLogger.CreateLogger<AcquiredRemoteResource>(), null);
                    return false;
                }

                lifetimeService = channel.GetServiceInstance<IRemoteResourceLifetimeServiceClient>(serviceRef, false);
            }

            lock (this.SyncRoot)
            {
                if (this.LifetimeService == null)
                {
                    this.LifetimeService = lifetimeService;
                }
                else
                {
                    lifetimeService = this.LifetimeService;
                }
            }


            var newOwnerId = await lifetimeService.TakeOwnershipAsync(this.ResourceId, this.AcquiredResourceCookie).ConfigureAwait(false);
            if (newOwnerId != RemoteResourceBase.InvalidOwnerId)
            {
                lock (this.SyncRoot)
                {
                    this.InitOwner(newOwnerId);
                }

                AcquiredResourceKeepAliveHelper.Add(this);
                return true;
            }

            return false;
        }

        [SuppressMessage("Design", "CA1033:Interface methods should be callable by child types", Justification = "<Pending>")]
        void IDisposable.Dispose()
        {
            this.BeginRelease();
        }

        internal void NotifyReleased()
        {
            lock (this.SyncRoot)
            {
                if (!this.isReleased)
                {
                    GC.SuppressFinalize(this);

                    this.isReleased = true;
                    this.OwnerId = RemoteResourceBase.InvalidOwnerId;
                    this.LifetimeService = null;
                }
            }
        }

        private void InitOwner(int ownerId)
        {
            if (this.isReleased)
            {
                throw new InvalidOperationException("Acquired resource has already been released.");
            }

            Debug.Assert(this.OwnerId == RemoteResourceBase.InvalidOwnerId, "TakeOwnership should only be called once.");
            this.OwnerId = ownerId;
        }
    }
}