using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Server.Internal
{
    public abstract class EventProducer<TService, TEventArgs> : EventProducerBase<TService> where TService : class
    {
        private readonly IRpcAsyncStreamWriter<TEventArgs> responseStream;

        private readonly object syncRoot = new object();

        private readonly Queue<Task<TEventArgs>> taskQueue = new Queue<Task<TEventArgs>>();

        private TaskCompletionSource<TEventArgs>? activeTcs = new TaskCompletionSource<TEventArgs>();

        private Task<TEventArgs> pendingTask;

        internal EventProducer(IRpcAsyncStreamWriter<TEventArgs> responseStream, CancellationToken cancellationToken)
            : base(cancellationToken)
        {
            this.responseStream = responseStream;
            this.pendingTask = this.activeTcs!.Task;
        }

        internal void HandleEvent(TEventArgs e)
        {
            TaskCompletionSource<TEventArgs>? tcs;
            lock (this.syncRoot)
            {
                tcs = this.activeTcs;
                if (tcs != null)
                {
                    this.activeTcs = new TaskCompletionSource<TEventArgs>();
                    this.taskQueue.Enqueue(this.activeTcs.Task);
                }
            }

            tcs?.SetResult(e);
        }

        [SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "Already awaited")]
        protected override async Task RunReceiveLoop(Task serviceUnpublishedTask)
        {
            try
            {
                // Write an empty EventArgs to notify client that we have added the event handler
                await this.responseStream.WriteAsync(
                    (TEventArgs)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(
                        typeof(TEventArgs))).ConfigureAwait(false);

                while (true)
                {
                    Task finishedTask;
                    var tcs = new TaskCompletionSource<bool>();
                    using (this.CancellationToken.Register(() => tcs.SetResult(true)))
                    {
                        finishedTask = await Task.WhenAny(this.pendingTask, tcs.Task, serviceUnpublishedTask).ContextFree();
                    }
                    // this.CancellationToken.ThrowIfCancellationRequested();
                    if (finishedTask == tcs.Task || this.CancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (finishedTask == serviceUnpublishedTask)
                    {
                        throw new RpcServiceUnavailableException("Service is no longer published.");
                    }

                    var eventArgs = this.pendingTask.Result;

                    await this.responseStream.WriteAsync(eventArgs).ConfigureAwait(false);

                    lock (this.syncRoot)
                    {
                        this.pendingTask = this.taskQueue.Dequeue();
                    }
                }
            }
            finally
            {
                lock (this.syncRoot)
                {
                    this.activeTcs = null;
                }
            }
        }
    }

    public abstract class EventProducerBase<TService> where TService : class
    {
        private readonly TaskCompletionSource<bool> stopTcs = new TaskCompletionSource<bool>();

        private Task? runTask;

        protected EventProducerBase(CancellationToken cancellationToken)
        {
            this.CancellationToken = cancellationToken;
        }

        protected CancellationToken CancellationToken { get; }
        protected Task StopTask => this.stopTcs.Task;

        internal Task Run(WeakReference<TService> service, Task serviceUnpublishedTask)
        {
            this.runTask = this.RunCore(service, serviceUnpublishedTask);

            return this.runTask;
        }

        internal Task StopAsync()
        {
            this.stopTcs.SetResult(true);
            return this.runTask ?? Task.CompletedTask;
        }

        protected abstract void AddDelegate(TService service);

        protected abstract void RemoveDelegate(TService service);

        [SuppressMessage("Style", "IDE0059:Unnecessary assignment of a value")]
        protected async Task RunCore(WeakReference<TService> wrService, Task serviceUnpublishedTask)
        {
            if (wrService != null && wrService.TryGetTarget(out var service))
            {
                this.AddDelegate(service);

                // It's vital that there's no reference to service when running the receive loop, to allow it to be GCed.
                // service will go out of scope, but I have seen longer variable lifetimes than expected in debug mode
                // (but maybe not in an async methof). To be safe, let's clear the variable.
                // (If assignment is optimized away, the lifetime will hopefully end here)
                service = null;
            }
            else
            {
                return;
            }

            try
            {
                await this.RunReceiveLoop(serviceUnpublishedTask).ContextFree();
            }
            finally
            {
                if (RpcStubOptions.TestDelayEventHandlers)
                {
                    await Task.Delay(100).ConfigureAwait(false);
                }

                if (wrService.TryGetTarget(out service))
                {
                    this.RemoveDelegate(service);
                }
            }
        }

        protected abstract Task RunReceiveLoop(Task serviceUnpublishedTask);
    }
}