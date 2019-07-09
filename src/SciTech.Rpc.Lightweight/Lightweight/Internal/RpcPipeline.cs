#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
//
// Based on SimplPipeline in SimplPipelines/SimplSockets by Marc Gravell (https://github.com/mgravell/simplsockets)
//
#endregion

using SciTech.IO;
using SciTech.Rpc.Logging;
using SciTech.Threading;
using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Internal
{
    internal abstract class RpcPipeline : IDisposable
    {
        private static readonly ILog Logger = LogProvider.For<RpcPipeline>();

        /// <summary>
        private readonly SemaphoreSlim singleWriter = new SemaphoreSlim(1);

        private CountedBufferWriterStream? activeWriteStream;

        private LightweightRpcFrame.WriteState currentWriteState;

        private int maxFrameLength = LightweightRpcFrame.DefaultMaxFrameLength;

        private IDuplexPipe? pipe;

        private readonly object syncRoot = new object();

        protected RpcPipeline(IDuplexPipe pipe)
        {
            this.pipe = pipe;
        }

        public void AbortWrite(Exception? exception)
        {
            var writer = this.pipe?.Output ?? throw new ObjectDisposedException(this.ToString());

            writer.CancelPendingFlush();
            writer.FlushAsync();

            // TODO: Write error response.
        }

        /// <summary>
        /// Begins writing a response frame. <see cref="EndWriteAsync"/> or <see cref="AbortWrite"/> must be called to finalize the write.
        /// </summary>
        /// <param name="responseHeader"></param>
        /// <returns></returns>
        public ValueTask<Stream> BeginWriteAsync(in LightweightRpcFrame responseHeader)
        {
            if (this.singleWriter.Wait(0))
            {
                var writer = this.pipe?.Output ?? throw new ObjectDisposedException(this.ToString());
                this.currentWriteState = responseHeader.BeginWrite(writer);
                this.activeWriteStream = new CountedBufferWriterStream(writer);
                return new ValueTask<Stream>(this.activeWriteStream);
            }

            var header = responseHeader;
            async ValueTask<Stream> AwaitSingleWriter()
            {
                await this.singleWriter.WaitAsync().ContextFree();
                var writer = this.pipe?.Output ?? throw new ObjectDisposedException(this.ToString());
                this.currentWriteState = header.BeginWrite(writer);
                this.activeWriteStream = new CountedBufferWriterStream(writer);
                return this.activeWriteStream;
            }

            return AwaitSingleWriter();
        }

        public void Close(Exception? ex = null)
        {
            IDuplexPipe? pipe;
            lock (this.syncRoot)
            {
                pipe = this.pipe;
                this.pipe = null;
            }

#pragma warning disable CA1031 // Do not catch general exception types
            if (pipe != null)
            {
                // burn the pipe to the ground
                try { pipe.Input.Complete(ex); } catch { }
                try { pipe.Input.CancelPendingRead(); } catch { }
                try { pipe.Output.Complete(ex); } catch { }
                try { pipe.Output.CancelPendingFlush(); } catch { }
                if (pipe is IDisposable d)
                {
                    try { d.Dispose(); } catch { }
                }
            }
            try { this.singleWriter.Dispose(); } catch { }
#pragma warning restore CA1031 // Do not catch general exception types
        }

        public void Dispose()
        {
            this.Dispose(true);
        }

        public ValueTask EndWriteAsync()
        {
            if (this.activeWriteStream == null)
            {
                throw new InvalidOperationException();
            }

            int payloadLength = checked((int)this.activeWriteStream.Length);

            LightweightRpcFrame.EndWrite(payloadLength, this.currentWriteState);

            var writer = this.pipe?.Output ?? throw new ObjectDisposedException(this.ToString());
            var flushTask = writer.FlushAsync();
            if (flushTask.IsCompletedSuccessfully)
            {
                this.activeWriteStream.Dispose();
                this.activeWriteStream = null;
                this.singleWriter.Release();
                return default;
            }

            async ValueTask AwaitFlushAndRelease()
            {
                await flushTask.ContextFree();

                // TODO: Should make sure that activeWriteStream is still non-null.
                // I.e. that EndWriteAsync is not called multiple times.
                this.activeWriteStream?.Dispose();
                this.activeWriteStream = null;
                this.singleWriter.Release();
            }

            return AwaitFlushAndRelease();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.Close();
            }
        }

        protected virtual ValueTask OnEndReceiveLoopAsync() => default;

        /// <summary>
        /// Called by receive loop when a new frame has arrived.
        /// </summary>
        /// <param name="frame"></param>
        /// <exception cref="Exception">If any exception is thrown by this method, the receive
        /// loop will be ended.</exception>
        /// <returns></returns>
        protected abstract ValueTask OnReceiveAsync(in LightweightRpcFrame frame);

        protected virtual ValueTask OnStartReceiveLoopAsync() => default;

        protected async Task StartReceiveLoopAsync(CancellationToken cancellationToken = default)
        {
#pragma warning disable CA1031 // Do not catch general exception types
            var reader = this.pipe?.Input ?? throw new ObjectDisposedException(this.ToString());
            try
            {
                await this.OnStartReceiveLoopAsync().ContextFree();
                bool makingProgress = false;
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!(makingProgress && reader.TryRead(out var readResult)))
                    {
                        readResult = await reader.ReadAsync(cancellationToken).ContextFree();
                    }

                    if (readResult.IsCanceled)
                    {
                        break;
                    }

                    var buffer = readResult.Buffer;

                    makingProgress = false;

                    if (LightweightRpcFrame.TryRead(ref buffer, this.maxFrameLength, out var frame))
                    {
                        makingProgress = true;
                        await this.OnReceiveAsync(frame).ContextFree();

                        // record that we comsumed up to the (now updated) buffer.Start,
                        // but we have not looked at anything after the updated start.
                        reader.AdvanceTo(buffer.Start);
                    }
                    else
                    {
                        // record that we comsumed up to the (NOT updated) buffer.Start,
                        // and tried to look at everything - hence buffer.End
                        reader.AdvanceTo(buffer.Start, buffer.End);
                    }

                    // exit the loop electively, or because we've consumed everything
                    // that we can usefully consume
                    if (!makingProgress && readResult.IsCompleted)
                    {
                        break;
                    }
                }
                try { reader.Complete(); } catch { }
            }
            catch (Exception ex)
            {
                try { reader.Complete(ex); } catch { }
            }
            finally
            {
                try { await this.OnEndReceiveLoopAsync().ContextFree(); } catch { }
            }
#pragma warning restore CA1031 // Do not catch general exception types
        }
    }
}
