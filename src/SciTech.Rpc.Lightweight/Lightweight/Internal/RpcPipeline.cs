#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB
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

using Microsoft.Extensions.ObjectPool;
using SciTech.Rpc.Logging;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Serialization.Internal;
using SciTech.Threading;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Internal
{
    internal abstract class RpcPipeline : ILightweightRpcFrameWriter, IAsyncDisposable
    {
        /// <summary>
        /// Mutex that provides single access for pipe writers. 
        /// NOTE: This mutex will not be disposed when the pipeline is disposed. Since 
        /// the <see cref="CloseAsync(Exception?)"/> method can be called concurrently, there's no easy way 
        /// of disposing the mutex correctly.
        /// </summary>
#pragma warning disable CA2213
        private readonly SemaphoreSlim singleWriter = new SemaphoreSlim(1);
#pragma warning restore CA2213

        private readonly bool skipLargeFrames;

        private readonly object syncRoot = new object();

        private volatile Task? receiveLoopTask;
        private CancellationTokenSource? receiveLoopCts = new CancellationTokenSource();
        //private LightweightRpcFrame.WriteState currentWriteState;

        //private BufferWriterStream? frameWriterStream;


        protected RpcPipeline(IDuplexPipe pipe, int? maxSendFrameLength, int? maxReceiveFrameLength, bool skipLargeFrames)
        {
            this.Pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
            this.MaxSendFrameLength = maxSendFrameLength ?? LightweightRpcFrame.DefaultMaxFrameLength;
            this.MaxReceiveFrameLength = maxReceiveFrameLength ?? LightweightRpcFrame.DefaultMaxFrameLength;
            this.skipLargeFrames = skipLargeFrames;
        }

        public event EventHandler<ExceptionEventArgs>? ReceiveLoopFaulted;

        public bool IsClosed
        {
            get
            {
                lock (this.syncRoot)
                {
                    return this.Pipe == null;
                }
            }
        }

        protected int MaxReceiveFrameLength { get; }

        protected int MaxSendFrameLength { get; }

        protected IDuplexPipe? Pipe { get; private set; }


        public void AbortWrite( in LightweightRpcFrame.WriteState writeState)
        {
            writeState.Writer.Reset();
            BufferWriterPool.Return((BufferWriterStreamImpl)writeState.Writer);
        }

        public Task WaitFinishedAsync()
        {
            return this.receiveLoopTask ?? Task.CompletedTask;
        }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cleanup")]
        public async Task CloseAsync(Exception? ex = null)
        {
            IDuplexPipe? pipe;
            CancellationTokenSource? receiveLoopCts;

            await this.singleWriter.WaitAsync().ContextFree();

            lock (this.syncRoot)
            {
                pipe = this.Pipe;
                this.Pipe = null;
                receiveLoopCts = this.receiveLoopCts;
                this.receiveLoopCts = null;
            }

            this.singleWriter.Release();

            if (pipe != null)
            {
                // burn the pipe to the ground
                try { await pipe.Input.CompleteAsync(ex).ContextFree(); } catch { }
                try { pipe.Input.CancelPendingRead(); } catch { }
                try { await pipe.Output.CompleteAsync(ex).ContextFree(); } catch { }
                try { pipe.Output.CancelPendingFlush(); } catch { }
                if (pipe is IDisposable d)
                {
                    try { d.Dispose(); } catch { }
                }

                receiveLoopCts?.Cancel();

                await this.WaitFinishedAsync().ContextFree();
                receiveLoopCts?.Dispose();

                this.OnClosed(ex);
            }            
        }

        public ValueTask DisposeAsync() => new ValueTask(this.CloseAsync());

        public ValueTask EndWriteAsync(in LightweightRpcFrame.WriteState writeState, bool throwOnError )
        {
            var frameWriter = writeState.Writer;

            try
            {
                var messageLength = frameWriter.Length;
                if (messageLength > this.MaxSendFrameLength)
                {
                    throw new RpcFailureException(RpcFailure.SizeLimitExceeded, $"RPC send message too large (messageSize={messageLength}, maxSize={this.MaxSendFrameLength}.");
                }

                LightweightRpcFrame.EndWrite((int)messageLength, writeState);
            }
            catch
            {
                this.AbortWrite(writeState);
                throw;
            }

            SemaphoreSlim writerMutex = this.singleWriter;
            var waitTask = writerMutex.WaitAsync();
            if (waitTask.Status == TaskStatus.RanToCompletion )
            {
                return FinalizeWriteAsync(this, frameWriter);
            }
            else
            {
                return AwaitWriterAndFinalizeAsync(this, waitTask, frameWriter);
            }

            static async ValueTask AwaitWriterAndFinalizeAsync(RpcPipeline self, Task writerWaitTask, BufferWriterStream frameWriter)
            {
                await writerWaitTask.ContextFree();
                await FinalizeWriteAsync(self, frameWriter).ContextFree();
            }

            static async ValueTask AwaitWriteAndReleaseAsync(RpcPipeline self, ValueTask<FlushResult> flushTask, BufferWriterStream frameWriter)
            {
                try
                {
                    await flushTask.ContextFree();
                }
                finally
                {
                    self.ReleaseWriteStream(frameWriter);
                }
            }

            static ValueTask FinalizeWriteAsync(RpcPipeline self, BufferWriterStream frameWriter)
            {
                try
                {
                    var pipeWriter = self.Pipe?.Output;
                    if (pipeWriter == null)
                    {
                        throw new ObjectDisposedException(nameof(RpcPipeline));
                    }

                    frameWriter.CopyTo(pipeWriter);
                    var flushTask = pipeWriter.FlushAsync();

                    if (flushTask.IsCompletedSuccessfully)
                    {
                        self.ReleaseWriteStream(frameWriter);
                        return default;
                    }
                    else
                    {
                        return AwaitWriteAndReleaseAsync(self, flushTask, frameWriter);
                    }
                }
                catch
                {
                    self.ReleaseWriteStream(frameWriter);
                    throw;
                }
            }
        }


        protected virtual void OnClosed(Exception? ex)
        {

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

        /// <summary>
        /// Called by receive loop when a large frame has arrived (i.e. a frame with 
        /// size &gt; <see cref="MaxReceiveFrameLength"/>). The frame only includes 
        /// <see cref="LightweightRpcFrame.MessageNumber"/>, <see cref="LightweightRpcFrame.FrameLength"/>,
        /// and <see cref="LightweightRpcFrame.FrameType"/>
        /// </summary>
        /// <param name="frame"></param>
        /// <exception cref="Exception">If any exception is thrown by this method, the receive
        /// loop will be ended.</exception>
        /// <returns></returns>
        protected abstract Task OnReceiveLargeFrameAsync(LightweightRpcFrame frame);

        protected virtual Task OnReceiveLoopFaultedAsync(ExceptionEventArgs e)
        {
            this.ReceiveLoopFaulted?.Invoke(this, e);

            return Task.CompletedTask;
        }

        protected virtual ValueTask OnStartReceiveLoopAsync() => default;

        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Silent receive loop.")]
        protected Task StartReceiveLoopAsync()
        {
            if (this.receiveLoopCts == null) throw new ObjectDisposedException(nameof(RpcPipeline));
            if (this.receiveLoopTask != null) throw new InvalidOperationException("Receive loop already started.");

            this.receiveLoopTask = ReceiveLoopAsync(this.receiveLoopCts.Token);
            return this.receiveLoopTask;

            async Task ReceiveLoopAsync(CancellationToken cancellationToken)
            {
                var reader = this.Pipe?.Input ?? throw new ObjectDisposedException(this.ToString());
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

                        switch (LightweightRpcFrame.TryRead(ref buffer, this.MaxReceiveFrameLength, out var frame))
                        {
                            case RpcFrameState.Full:

                                makingProgress = true;
                                await this.OnReceiveAsync(frame).ContextFree();

                                // record that we consumed up to the (now updated) buffer.Start,
                                // but we have not looked at anything after the updated start.
                                reader.AdvanceTo(buffer.Start);
                                break;
                            case RpcFrameState.Header:
                                if (frame.FrameLength > this.MaxReceiveFrameLength)
                                {
                                    if (this.skipLargeFrames)
                                    {
                                        makingProgress = true; // There might be another frame after this one.
                                        await this.OnReceiveLargeFrameAsync(frame).ContextFree();

                                        int bytesToSkip = frame.FrameLength.Value;
                                        var advanceBytes = (int)Math.Min(buffer.Length, bytesToSkip);
                                        reader.AdvanceTo(buffer.Slice(advanceBytes).Start);
                                        bytesToSkip -= advanceBytes;
                                        if (bytesToSkip > 0)
                                        {
                                            await SkipSequenceBytes(reader, bytesToSkip, cancellationToken).ContextFree();
                                        }
                                    }
                                    else
                                    {
                                        throw new RpcCommunicationException(RpcCommunicationStatus.Unavailable,//  RpcFailure.SizeLimitExceeded,
                                            $"Size of received frame exceeds size limit (frame size={frame.FrameLength}, max size={this.MaxReceiveFrameLength}).");
                                    }
                                    break;
                                }
                                goto case RpcFrameState.None;
                            case RpcFrameState.None:
                                // record that we consumed up to the (NOT updated) buffer.Start,
                                // and tried to look at everything - hence buffer.End
                                reader.AdvanceTo(buffer.Start, buffer.End);
                                break;
                        }

                        // exit the loop electively, or because we've consumed everything
                        // that we can usefully consume
                        if (!makingProgress && readResult.IsCompleted)
                        {
                            if (!this.IsClosed)
                            {
                                // If we get here while not closed it indicates that
                                // the connection has been lost.
                                // On the the server side this is expected when a client disconnects.
                                // On the client side it indicates that connection to the server has been lost.
                                throw new RpcCommunicationException(RpcCommunicationStatus.ConnectionLost);
                            }
                            break;
                        }
                    }
                    try {await reader.CompleteAsync().ContextFree(); } catch { }
                }
                catch (Exception ex)
                {                    
                    try { await reader.CompleteAsync(ex).ContextFree(); } catch { }
                    try { await this.OnReceiveLoopFaultedAsync(new ExceptionEventArgs(ex)).ContextFree(); } catch { }
                }
                finally
                {
                    try { await this.OnEndReceiveLoopAsync().ContextFree(); } catch { }
                }
            }
        }

        private static async Task SkipSequenceBytes(PipeReader reader, int bytesToSkip, CancellationToken cancellationToken)
        {
            while (bytesToSkip > 0)
            {
                var skipResult = await reader.ReadAsync(cancellationToken).ContextFree();
                if (skipResult.IsCanceled || skipResult.IsCompleted)
                {
                    break;
                }

                var skipBuffer = skipResult.Buffer;
                var advanceBytes = (int)Math.Min(skipBuffer.Length, bytesToSkip);
                if (advanceBytes > 0)
                {
                    skipBuffer = skipBuffer.Slice(advanceBytes);
                    reader.AdvanceTo(skipBuffer.Start);
                    bytesToSkip -= advanceBytes;
                }
            }
        }

        private static readonly ObjectPool<BufferWriterStreamImpl> BufferWriterPool = ObjectPool.Create<BufferWriterStreamImpl>();

        /// <summary>
        /// Begins writing a response frame. <see cref="EndWriteAsync"/> or <see cref="AbortWrite"/> must be called to finalize the write.
        /// </summary>
        /// <param name="responseHeader"></param>
        /// <returns></returns>
        public LightweightRpcFrame.WriteState BeginWrite(in LightweightRpcFrame responseHeader)
        {
            var writer = BufferWriterPool.Get();
            return responseHeader.BeginWrite(writer);
        }

        private void ReleaseWriteStream(BufferWriterStream writerStream)
        {
            writerStream.Reset();
            BufferWriterPool.Return((BufferWriterStreamImpl)writerStream);

            this.singleWriter.Release();
        }
    }
}
