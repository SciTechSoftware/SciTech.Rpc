using System.Collections.Immutable;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace SciTech.Rpc.Resources
{
    public enum ReaderWriterLockState
    {
        NoLock,
        ReadLock,
        WriteLock
    }

    [DataContract]
    public sealed class AcquiredRemoteLock : AcquiredRemoteResource
    {
        [JsonConstructor]
        internal AcquiredRemoteLock() { }

        internal AcquiredRemoteLock(AcquiredRemoteResource lockResource)
            : base(lockResource.LifetimeServiceRef, lockResource.ResourceId, lockResource.AcquiredResourceCookie)
        {
        }
    }

    public class LocksReleasedEventArgs : EventArgs
    {
        public LocksReleasedEventArgs(ImmutableArray<object> lockDatas, ReaderWriterLockState lockState)
        {
            this.LockState = lockState;
            this.LockDatas = lockDatas;
        }

        public ImmutableArray<object> LockDatas { get; }

        public ReaderWriterLockState LockState { get; }
    }

    public class LockStateEventArgs : EventArgs
    {
        public LockStateEventArgs(ReaderWriterLockState lockState)
        {
            this.LockState = lockState;
        }

        public ReaderWriterLockState LockState { get; }
    }

    /// <summary>
    /// </summary>
    public class RemoteReaderWriterLock
    {
        private readonly RemoteResource readerLocksResource;

        private readonly object syncRoot = new object();

        private readonly RemoteResource writerLockResource;

        private ReaderWriterLockState lockState;

        public RemoteReaderWriterLock(RemoteResourceLifetimeManager lifetimeManager)
        {
            this.readerLocksResource = new RemoteResource(lifetimeManager, int.MaxValue, this.syncRoot);
            this.writerLockResource = new RemoteResource(lifetimeManager, 1, this.syncRoot);

            this.readerLocksResource.AcquiredChanged += this.LockResource_AcquiredChanged;
            this.writerLockResource.AcquiredChanged += this.LockResource_AcquiredChanged;
        }

        // public event LocksReleasedEventHandler LocksReleased;

        public event EventHandler<LockStateEventArgs>? LockStateChanged;

        public ReaderWriterLockState LockState
        {
            get
            {
                lock (this.syncRoot)
                {
                    if (this.writerLockResource.IsAcquiredSynchronized)
                    {
                        return ReaderWriterLockState.WriteLock;
                    }

                    if (this.readerLocksResource.IsAcquiredSynchronized)
                    {
                        return ReaderWriterLockState.ReadLock;
                    }

                    return ReaderWriterLockState.NoLock;
                }
            }
        }

        public AcquiredRemoteLock? AcquireReaderLock()
        {
            return this.AcquireReaderLock(null);
        }

        /// <summary>
        /// Tries to acquire a reader lock. If the lock succeeds an AccquiredRemoteLock is returned,
        /// otherwise <c>null</c> is returned.
        /// </summary>
        /// <param name="lockData"></param>
        /// <returns></returns>
        public AcquiredRemoteLock? AcquireReaderLock(object? lockData)
        {
            AcquiredRemoteLock? retVal = null;
            ReaderWriterLockState newLockState = ReaderWriterLockState.NoLock;
            bool lockStateChanged = false;
            lock (this.syncRoot)
            {
                if (!this.writerLockResource.IsAcquired)
                {
                    var (acquiredLockResource, _) = this.readerLocksResource.AcquireRemoteResourceCoreSynchronized();

                    retVal = acquiredLockResource != null ? new AcquiredRemoteLock(acquiredLockResource) : null;

                    newLockState = this.RetrieveLockStateSynchronized();
                    if (newLockState != this.lockState)
                    {
                        lockStateChanged = true;
                        this.lockState = newLockState;
                    }
                }
            }

            if (lockStateChanged)
            {
                this.OnLockStateChanged(new LockStateEventArgs(newLockState));
            }

            return retVal;
        }

        public AcquiredRemoteLock? AcquireWriterLock()
        {
            return this.AcquireWriterLock(null);
        }

        public AcquiredRemoteLock? AcquireWriterLock(object? lockData)
        {
            AcquiredRemoteLock? retVal = null;
            ReaderWriterLockState newLockState = ReaderWriterLockState.NoLock;
            bool lockStateChanged = false;

            lock (this.syncRoot)
            {
                if (!this.writerLockResource.IsAcquired && !this.readerLocksResource.IsAcquired)
                {
                    var (acquiredLockResource, _) = this.writerLockResource.AcquireRemoteResourceCoreSynchronized();

                    retVal = acquiredLockResource != null ? new AcquiredRemoteLock(acquiredLockResource) : null;

                    newLockState = this.RetrieveLockStateSynchronized();
                    if (newLockState != this.lockState)
                    {
                        lockStateChanged = true;
                        this.lockState = newLockState;
                    }
                }
            }

            if (lockStateChanged)
            {
                this.OnLockStateChanged(new LockStateEventArgs(newLockState));
            }

            return retVal;
        }

        private void LockResource_AcquiredChanged(object? sender, EventArgs e)
        {
            this.UpdateLockState();
        }

        private void OnLockStateChanged(LockStateEventArgs e)
        {
            this.LockStateChanged?.Invoke(this, e);
        }

        private ReaderWriterLockState RetrieveLockStateSynchronized()
        {
            if (this.writerLockResource.IsAcquiredSynchronized)
            {
                return ReaderWriterLockState.WriteLock;
            }

            if (this.readerLocksResource.IsAcquiredSynchronized)
            {
                return ReaderWriterLockState.ReadLock;
            }

            return ReaderWriterLockState.NoLock;
        }

        private void UpdateLockState()
        {
            ReaderWriterLockState newLockState = ReaderWriterLockState.NoLock;
            bool lockStateChanged = false;

            lock (this.syncRoot)
            {
                newLockState = this.RetrieveLockStateSynchronized();
                if (newLockState != this.lockState)
                {
                    this.lockState = newLockState;
                    lockStateChanged = true;
                }
            }

            if (lockStateChanged)
            {
                this.OnLockStateChanged(new LockStateEventArgs(newLockState));
            }
        }
    }
}