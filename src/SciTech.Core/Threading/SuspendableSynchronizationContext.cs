#nullable enable 

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace SciTech.Threading
{
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Suspend/Resume is the natural pairing, and the API is already published.")]
    public interface ISuspendableSynchronizationContext
    {
        void PostImmediate(SendOrPostCallback d, object? state);

        void Suspend();

        void Resume();
    }

    public class SuspendableSynchronizationContext : SynchronizationContext,ISuspendableSynchronizationContext
    {
        SynchronizationContext baseContext;

        volatile int m_suspendCount;
        List<InvokeEntry> m_pendingEntries = new List<InvokeEntry>();
        object m_syncRoot = new object();

        [ThreadStatic]
        private static SuspendableSynchronizationContext? current;

        public SuspendableSynchronizationContext(SynchronizationContext baseContext)
        {
            this.baseContext = baseContext ?? throw new ArgumentNullException(nameof(baseContext));
        }

        public static new SuspendableSynchronizationContext? Current
        {
            get
            {
                var c = current;
                SynchronizationContext? baseContext = SynchronizationContext.Current;
                if( c?.baseContext != baseContext )
                {
                    c = baseContext != null ? new SuspendableSynchronizationContext(baseContext) : null;
                    current = c;
                }

                return c;
            }
        }


        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (m_syncRoot)
            {
                if (m_suspendCount > 0)
                {
                    m_pendingEntries.Add(new InvokeEntry(d, state));
                    return;
                }
            }

            this.baseContext.Post(d, state);
        }

        public void PostImmediate(SendOrPostCallback d, object? state)
        {
            this.baseContext.Post(d, state);
        }

        public void Suspend()
        {
            lock (m_syncRoot)
            {
                m_suspendCount++;
            }
        }

        public void Resume()
        {
            InvokeEntry[]? invokeEntries = null;
            lock (m_syncRoot)
            {
                m_suspendCount--;
                if (m_suspendCount < 0)
                    throw new InvalidOperationException("Suspend/Resume mismatch.");

                if (m_suspendCount == 0)
                {
                    invokeEntries = m_pendingEntries.ToArray();
                    m_pendingEntries.Clear();
                }
            }

            if (invokeEntries != null)
            {
                foreach (InvokeEntry ie in invokeEntries)
                {
                    this.baseContext.Post(ie.m_delegate, ie.m_state);
                }
            }

        }

        private sealed class InvokeEntry
        {
            internal SendOrPostCallback m_delegate;
            internal object? m_state;

            internal InvokeEntry(SendOrPostCallback d, object? state)
            {
                m_delegate = d;
                m_state = state;
            }
        }
    }
}