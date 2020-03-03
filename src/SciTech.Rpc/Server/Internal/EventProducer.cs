using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Server.Internal
{
    public abstract class EventProducer<TService, TEventArgs> : EventProducerBase<TService>
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

        protected async Task RunReceiveLoop()
        {
            try
            {
                // Write an empty EventArgs to notify client that we have added the event handler
                await this.responseStream.WriteAsync(
                    (TEventArgs)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(
                        typeof(TEventArgs))).ConfigureAwait(false);

                while (true)
                {
                    var tcs = new TaskCompletionSource<bool>();
                    using (this.CancellationToken.Register(() => tcs.SetResult(true)))
                    {
                        await Task.WhenAny(this.pendingTask, tcs.Task).ContextFree();
                    }
                    // this.CancellationToken.ThrowIfCancellationRequested();
                    if (this.CancellationToken.IsCancellationRequested)
                    {
                        break;
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

    public abstract class EventProducerBase<TService>
    {
        private readonly TaskCompletionSource<bool> stopTcs = new TaskCompletionSource<bool>();

        private Task? runTask;

        protected CancellationToken CancellationToken { get; }

        protected EventProducerBase(CancellationToken cancellationToken)
        {
            this.CancellationToken = cancellationToken;

        }

        protected Task StopTask => this.stopTcs.Task;

        internal Task Run(TService service)
        {
            this.runTask = this.RunImpl(service);

            return this.runTask;
        }

        internal Task StopAsync()
        {
            this.stopTcs.SetResult(true);
            return this.runTask ?? Task.CompletedTask;
        }

        protected abstract Task RunImpl(TService service);
    }
}
