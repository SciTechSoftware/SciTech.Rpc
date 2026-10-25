using SciTech.Diagnostics;
using SciTech.Rpc.Logging;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Resources
{
    internal static class AcquiredResourceKeepAliveHelper
    {
        private static readonly HashSet<AcquiredRemoteResource> pendingKeepAliveResources = new HashSet<AcquiredRemoteResource>();

        private static readonly List<WeakReference<AcquiredRemoteResource>> remoteResources = new List<WeakReference<AcquiredRemoteResource>>();

        private static readonly object syncRoot = new object();

        private static readonly TimeSpan TimerInterval = TimeSpan.FromSeconds(30);

        private static Timer? timer;

        internal static void Add(AcquiredRemoteResource remoteResource)
        {
            lock (syncRoot)
            {
                RemoveImpl(remoteResource);

                remoteResources.Add(new WeakReference<AcquiredRemoteResource>(remoteResource));
                if (remoteResources.Count == 1)
                {
                    timer = new Timer(TimerCallback, null, TimerInterval, TimerInterval);
                }
            }
        }

        internal static void PerformCleanup()
        {
            lock (syncRoot)
            {
                RemoveImpl(null);
            }
        }

        internal static void Remove(AcquiredRemoteResource remoteResource)
        {
            lock (syncRoot)
            {
                RemoveImpl(remoteResource);
            }
        }

        private static async Task NotifyAlive(AcquiredRemoteResource acquiredResource)
        {
            var lifetimeService = acquiredResource.LifetimeService;

            bool isAlive = false;
            if (lifetimeService != null)
            {
                try
                {
                    isAlive = await lifetimeService.KeepAliveAsync(acquiredResource.ResourceId, acquiredResource.AcquiredResourceCookie, acquiredResource.OwnerId).ConfigureAwait(false);
                }
                catch (Exception e) when (!e.IsFatal())
                {
                    ResourcesLog.KeepAliveFailed(RpcLogger.CreateLogger(typeof(AcquiredResourceKeepAliveHelper)), e);
                }
            }

            if (!isAlive)
            {
                ResourcesLog.AcquiredResourceLost(RpcLogger.CreateLogger(typeof(AcquiredResourceKeepAliveHelper)), null);
                acquiredResource.NotifyReleased();

                // Make sure this resource is removed. There's no need to call Release,
                // since either we cannot reach the host that owns the resource or the host
                // has told us that the we no longer own the resource.
                Remove(acquiredResource);
            }
            else
            {
                lock (syncRoot)
                {
                    pendingKeepAliveResources.Remove(acquiredResource);
                }
            }
        }

        private static void RemoveImpl(AcquiredRemoteResource? remoteResource)
        {
            for (int i = remoteResources.Count - 1; i >= 0; i--)
            {
                remoteResources[i].TryGetTarget(out var arl);
                if (arl == null || arl == remoteResource)
                {
                    remoteResources.RemoveAt(i);

                    if (arl != null)
                    {
                        pendingKeepAliveResources.Remove(arl);
                    }
                }
            }

            if (remoteResources.Count == 0)
            {
                timer?.Dispose();
                timer = null;
            }
        }

        private static void TimerCallback(object? state)
        {
            try
            {
                var resourcesToKeepAlive = new List<AcquiredRemoteResource>();

                lock (syncRoot)
                {
                    for (int i = remoteResources.Count; --i >= 0;)
                    {
                        if (remoteResources[i].TryGetTarget(out var arl))
                        {
                            if (pendingKeepAliveResources.Add(arl))
                            {
                                resourcesToKeepAlive.Add(arl);
                            }
                        }
                        else
                        {
                            remoteResources.RemoveAt(i);
                        }
                    }
                }

                foreach (var arl in resourcesToKeepAlive)
                {
                    bool doKeepAlive;
                    lock (syncRoot)
                    {
                        doKeepAlive = pendingKeepAliveResources.Contains(arl);
                    }

                    if (doKeepAlive)
                    {
                        NotifyAlive(arl).Forget();
                    }
                }
            }
            catch (Exception e) when (!e.IsFatal())
            {
                // An exception must not propagate out of a timer callback, since it would terminate the process.
                ResourcesLog.KeepAliveTimerFailed(RpcLogger.CreateLogger(typeof(AcquiredResourceKeepAliveHelper)), e);
            }
        }
    }
}