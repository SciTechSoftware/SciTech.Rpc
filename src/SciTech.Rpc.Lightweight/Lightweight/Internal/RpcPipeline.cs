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

using SciTech.Rpc.Lightweight.IO;
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
        /// Mutex that provides single access for pipe writers. 
        /// NOTE: This mutex will not be disposed when the pipeline is disposed. Since 
        /// the <see cref="Close"/> method can be called concurrently, there's no easy way 
        /// of disposing the mutex correctly.
        /// </summary>
#pragma warning disable CA2213 // Disposable fields should be disposed
        private readonly SemaphoreSlim singleWriter = new SemaphoreSlim(1);

#pragma warning restore CA2213 // Disposable fields should be disposed


        private readonly object syncRoot = new object();

        private LightweightRpcFrame.WriteState currentWriteState;

        private BufferWriterStream? frameWriterStream;

        private int maxReceiveFrameLength;

        private int maxSendFrameLength;

        private IDuplexPipe? pipe;

        protected RpcPipeline(IDuplexPipe pipe, int? maxSendFrameLength = null, int? maxReceiveFrameLength = null)
        {
            this.pipe = pipe;
            this.maxSendFrameLength = maxSendFrameLength ?? LightweightRpcFrame.DefaultMaxFrameLength;
            this.maxReceiveFrameLength = maxReceiveFrameLength ?? LightweightRpcFrame.DefaultMaxFrameLength;
            this.frameWriterStream = new BufferWriterStream();
        }

        public bool IsClosed
        {
            get
            {
                lock (this.syncRoot)
                {
                    return this.pipe == null;
                }
            }
        }

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
                pipe = this.pipe;
                this.pipe = null;
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

        public async ValueTask EndWriteAsync()
        {
            var pipeWriter = this.pipe?.Output;
            var frameWriter = this.frameWriterStream;
            if (frameWriter == null || pipeWriter == null)
            {
                throw new InvalidOperationException();
            }

            try
            {
                try
                {
                    int messageLength = checked((int)frameWriter.Length);

                    LightweightRpcFrame.EndWrite(messageLength, this.currentWriteState);
                    if (messageLength > this.maxSendFrameLength)
                    {
                        throw new RpcFailureException($"RPC message too large (messageSize={messageLength}, maxSize={this.maxSendFrameLength}.");
                    }
                }
                catch
                {
                    frameWriter.Reset();
                    throw;
                }

                frameWriter.CopyTo(pipeWriter);
                await pipeWriter.FlushAsync().ContextFree();
            }
            finally
            {
                this.ReleaseWriteStream();
            }
        }
        //public ValueTask EndWriteAsyncFast()
        //{
        //    var pipeWriter = this.pipe?.Output;
        //    var frameWriter = this.frameWriterStream;
        //    if (frameWriter == null || pipeWriter == null)
        //    {
        //        throw new InvalidOperationException();
        //    }
        //    ValueTask<FlushResult> flushTask;
        //    try
        //    {
        //        try
        //        {
        //            int frameLength = checked((int)frameWriter.Length);
        //            LightweightRpcFrame.EndWrite(frameLength, this.currentWriteState);
        //            if (frameLength > this.maxSendFrameLength)
        //            {
        //                throw new RpcFailureException($"RPC message too large (messageSize={frameLength}, maxSize={this.maxSendFrameLength}.");
        //            }
        //        }
        //        catch
        //        {
        //            writer.CancelPendingFlush();
        //            flushTask = writer.FlushAsync();
        //            throw;
        //        }
        //        flushTask = writer.FlushAsync();
        //        if (flushTask.IsCompletedSuccessfully)
        //        {
        //            this.ReleaseWriteStream();
        //            return default;
        //        }
        //    }
        //    catch
        //    {
        //        this.ReleaseWriteStream();
        //        throw;
        //    }
        //    async ValueTask AwaitFlushAndRelease()
        //    {
        //        try
        //        {
        //            await flushTask.ContextFree();
        //        }
        //        finally
        //        {
        //            this.ReleaseWriteStream();
        //        }
        //    }
        //    return AwaitFlushAndRelease();
        //}

        protected internal ValueTask<Stream> BeginWriteAsync(in LightweightRpcFrame responseHeader)
        {
            return this.BeginWriteAsync(responseHeader, true)!;
        }

        protected internal ValueTask<Stream?> TryBeginWriteAsync(in LightweightRpcFrame responseHeader)
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

        protected virtual void OnReceiveLoopFaulted(Exception e) { }

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

                    if (LightweightRpcFrame.TryRead(ref buffer, this.maxReceiveFrameLength, out var frame))
                    {
                        makingProgress = true;
                        await this.OnReceiveAsync(frame).ContextFree();

                        // record that we consumed up to the (now updated) buffer.Start,
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
                try { reader.Complete(ex); } catch { }
                try { this.OnReceiveLoopFaulted(ex); } catch { }
            }
            finally
            {
                try { await this.OnEndReceiveLoopAsync().ContextFree(); } catch { }
            }
#pragma warning restore CA1031 // Do not catch general exception types
        }

        /// <summary>
        /// Begins writing a response frame. <see cref="EndWriteAsync"/> or <see cref="AbortWrite"/> must be called to finalize the write.
        /// </summary>
        /// <param name="responseHeader"></param>
        /// <returns></returns>
        private ValueTask<Stream?> BeginWriteAsync(in LightweightRpcFrame responseHeader, bool throwOnError)
        {
            SemaphoreSlim? writerMutex = this.singleWriter;
            if (writerMutex.Wait(0))
            {
                try
                {
                    var writer = this.frameWriterStream;
                    if (writer == null)
                    {
                        return !throwOnError ? new ValueTask<Stream?>((Stream?)null) : throw new ObjectDisposedException(this.ToString());
                    }

                    this.currentWriteState = responseHeader.BeginWrite(writer);
                    writerMutex = null; // Prevent mutex from being released (will be released in EndWrite/AbortWrite
                    return new ValueTask<Stream?>(writer);
                }
                finally
                {
                    writerMutex?.Release();
                }
            }

            var header = responseHeader;
            async ValueTask<Stream?> AwaitSingleWriter()
            {
                await writerMutex.WaitAsync().ContextFree();
                try
                {
                    var writer = this.frameWriterStream;
                    if (writer == null)
                    {
                        return !throwOnError ? (Stream?)null : throw new ObjectDisposedException(this.ToString());
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
