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

using SciTech.Rpc.Logging;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Serialization.Internal;
using SciTech.Threading;
using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Internal
{
    internal class ExceptionEventArgs : EventArgs
    {
        public ExceptionEventArgs(Exception exception)
        {
            this.Exception = exception;
        }

        public Exception Exception { get; }
    }

    internal abstract class RpcPipeline : IDisposable
    {
        private static readonly ILog Logger = LogProvider.For<RpcPipeline>();

        /// <summary>
        /// Mutex that provides single access for pipe writers. 
        /// NOTE: This mutex will not be disposed when the pipeline is disposed. Since 
        /// the <see cref="Close"/> method can be called concurrently, there's no easy way 
        /// of disposing the mutex correctly.
        /// </summary>
#pragma warning disable CA2213 // Disposable fields should be disposed
        private readonly SemaphoreSlim singleWriter = new SemaphoreSlim(1);

#pragma warning restore CA2213 // Disposable fields should be disposed


        private readonly bool skipLargeFrames;

        private readonly object syncRoot = new object();

        private LightweightRpcFrame.WriteState currentWriteState;

        private BufferWriterStream? frameWriterStream;


        protected RpcPipeline(IDuplexPipe pipe, int? maxSendFrameLength, int? maxReceiveFrameLength, bool skipLargeFrames)
        {
            this.Pipe = pipe;
            this.MaxSendFrameLength = maxSendFrameLength ?? LightweightRpcFrame.DefaultMaxFrameLength;
            this.MaxReceiveFrameLength = maxReceiveFrameLength ?? LightweightRpcFrame.DefaultMaxFrameLength;
            this.skipLargeFrames = skipLargeFrames;
            this.frameWriterStream = new BufferWriterStreamImpl();
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


        public void AbortWrite()
        {
            this.ReleaseWriteStream();
        }

        public void Close(Exception? ex = null)
        {
            IDuplexPipe? pipe;
            BufferWriterStream? frameWriterStream;

            lock (this.syncRoot)
            {
                pipe = this.Pipe;
                this.Pipe = null;
            }
            this.singleWriter.Wait();

            frameWriterStream = this.frameWriterStream;
            this.frameWriterStream = null;

            this.singleWriter.Release();

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

                this.OnClosed(ex);
            }

            frameWriterStream?.Dispose();

#pragma warning restore CA1031 // Do not catch general exception types
        }

        public void Dispose()
        {
            this.Dispose(true);
        }

        public ValueTask EndWriteAsync()
        {
            var pipeWriter = this.Pipe?.Output;
            var frameWriter = this.frameWriterStream;
            if (frameWriter == null || pipeWriter == null)
            {
                throw new InvalidOperationException();
            }

            static async ValueTask AwaitWriteAndRelease(RpcPipeline self, ValueTask<FlushResult> flushTask )
            {
                try
                {
                    await flushTask.ContextFree();
                }
                finally
                {
                    self.ReleaseWriteStream();
                }
            }

            bool writeStreamReleased = false;
            try
            {
                try
                {
                    int messageLength = checked((int)frameWriter.Length);

                    LightweightRpcFrame.EndWrite(messageLength, this.currentWriteState);
                    if (messageLength > this.MaxSendFrameLength)
                    {
                        throw new RpcFailureException(RpcFailure.SizeLimitExceeded, $"RPC send message too large (messageSize={messageLength}, maxSize={this.MaxSendFrameLength}.");
                    }
                }
                catch
                {
                    frameWriter.Reset();
                    throw;
                }

                frameWriter.CopyTo(pipeWriter);
                var flushTask = pipeWriter.FlushAsync();
                writeStreamReleased = true; // It will be soon

                if ( flushTask.IsCompletedSuccessfully)
                {
                    this.ReleaseWriteStream();
                    return default;

                } else
                {
                    return AwaitWriteAndRelease(this, flushTask);
                }
            }
            finally
            {
                if (!writeStreamReleased)
                {
                    this.ReleaseWriteStream();
                }
            }
        }



        protected internal ValueTask<BufferWriterStream> BeginWriteAsync(in LightweightRpcFrame responseHeader)
        {
            return this.BeginWriteAsync(responseHeader, true)!;
        }

        protected internal ValueTask<BufferWriterStream?> TryBeginWriteAsync(in LightweightRpcFrame responseHeader)
        {
            return this.BeginWriteAsync(responseHeader, false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.Close();
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

        protected virtual void OnReceiveLoopFaulted(ExceptionEventArgs e)
        {
            this.ReceiveLoopFaulted?.Invoke(this, e);
        }

        protected virtual ValueTask OnStartReceiveLoopAsync() => default;

        protected async Task StartReceiveLoopAsync(CancellationToken cancellationToken = default)
        {
#pragma warning disable CA1031 // Do not catch general exception types
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
                            // the server side connection has been lost.
                            throw new RpcCommunicationException(RpcCommunicationStatus.ConnectionLost);
                        }

                        break;
                    }
                }
                try { reader.Complete(); } catch { }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "RpcPipeline receive loop ended with error '{Error}'", ex.Message);

                try { reader.Complete(ex); } catch { }
                try { this.OnReceiveLoopFaulted(new ExceptionEventArgs(ex)); } catch { }
            }
            finally
            {
                try { await this.OnEndReceiveLoopAsync().ContextFree(); } catch { }
            }
#pragma warning restore CA1031 // Do not catch general exception types
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

        /// <summary>
        /// Begins writing a response frame. <see cref="EndWriteAsync"/> or <see cref="AbortWrite"/> must be called to finalize the write.
        /// </summary>
        /// <param name="responseHeader"></param>
        /// <returns></returns>
        private ValueTask<BufferWriterStream?> BeginWriteAsync(in LightweightRpcFrame responseHeader, bool throwOnError)
        {
            SemaphoreSlim? writerMutex = this.singleWriter;
            if (writerMutex.Wait(0))
            {
                try
                {
                    var writer = this.frameWriterStream;
                    if (writer == null)
                    {
                        return !throwOnError ? new ValueTask<BufferWriterStream?>((BufferWriterStream?)null) : throw new ObjectDisposedException(this.ToString());
                    }

                    this.currentWriteState = responseHeader.BeginWrite(writer);
                    writerMutex = null; // Prevent mutex from being released (will be released in EndWrite/AbortWrite
                    return new ValueTask<BufferWriterStream?>(writer);
                }
                finally
                {
                    writerMutex?.Release();
                }
            }

            var header = responseHeader;
            async ValueTask<BufferWriterStream?> AwaitSingleWriter()
            {
                await writerMutex.WaitAsync().ContextFree();
                try
                {
                    var writer = this.frameWriterStream;
                    if (writer == null)
                    {
                        return !throwOnError ? (BufferWriterStream?)null : throw new ObjectDisposedException(this.ToString());
                    }

                    this.currentWriteState = header.BeginWrite(writer);
                    writerMutex = null; // Prevent mutex from being released (will be released in EndWrite/AbortWrite
                    return writer;
                }
                finally
                {
                    writerMutex?.Release();
                }
            }

            return AwaitSingleWriter();
        }

        private void ReleaseWriteStream()
        {
            this.frameWriterStream!.Reset();
            this.singleWriter.Release();
        }
    }
}
