#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.IO
{
    /// <summary>
    /// A combined writer Stream and IBufferWriter{byte}.
    /// TODO: If this class is kept, unit tests must be written.
    /// </summary>
    internal sealed class BufferWriterStream : Stream, IBufferWriter<byte>
    {
        private byte[]? activeBuffer;

        private ArrayPool<byte> arrayPool;

        private int availableBufferSize;

        private int chunkSize;

        private List<FilledBuffer>? filledBuffers;

        private long length;

        internal BufferWriterStream(int chunkSize = 16384, ArrayPool<byte>? arrayPool = null)
        {
            this.arrayPool = arrayPool ?? ArrayPool<byte>.Shared;
            this.chunkSize = chunkSize;
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => this.length;

        public override long Position { get => this.length; set => throw new NotSupportedException(); }

        public void Advance(int count)
        {
            if (count < 0 || count > this.availableBufferSize)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            this.availableBufferSize -= count;
            this.length += count;
        }

        public override void Close()
        {
            base.Close();

            this.Reset();
            if (this.activeBuffer != null)
            {
                this.arrayPool.Return(this.activeBuffer);
                this.activeBuffer = null;
            }

            this.availableBufferSize = 0;
        }

        public void CopyTo(IBufferWriter<byte> writer)
        {
            if (this.filledBuffers != null)
            {
                foreach (var buffer in this.filledBuffers)
                {
                    writer.Write(new ReadOnlySpan<byte>(buffer.buffer, 0, buffer.bytesInBuffer));
                }
            }

            if (this.activeBuffer != null)
            {
                int bytesInBuffer = this.activeBuffer.Length - this.availableBufferSize;
                if (bytesInBuffer > 0)
                {
                    writer.Write(new Span<byte>(this.activeBuffer, 0, bytesInBuffer));
                }
            }
        }

        public override void Flush()
        {
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            if (this.availableBufferSize == 0 || this.availableBufferSize < sizeHint)
            {
                this.AllocateBuffer(sizeHint);
            }

            return new Memory<byte>(this.activeBuffer, this.activeBuffer!.Length - this.availableBufferSize, this.availableBufferSize);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            if (this.availableBufferSize == 0 || this.availableBufferSize < sizeHint)
            {
                this.AllocateBuffer(sizeHint);
            }

            return new Span<byte>(this.activeBuffer, this.activeBuffer!.Length - this.availableBufferSize, this.availableBufferSize);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public void Reset()
        {
            if (this.filledBuffers != null)
            {
                foreach (var buffer in this.filledBuffers)
                {
                    this.arrayPool.Return(buffer.buffer);
                }

                this.filledBuffers.Clear();
            }

            this.availableBufferSize = this.activeBuffer?.Length ?? 0;
            this.length = 0;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            int srcOffset = offset;
            int bytesLeft = count;
            while (bytesLeft > 0)
            {
                if (this.availableBufferSize == 0)
                {
                    this.AllocateBuffer(0);
                }

                int dstOffset = this.activeBuffer!.Length - this.availableBufferSize;
                int bytesToCopy = Math.Min(bytesLeft, this.availableBufferSize);
                Buffer.BlockCopy(buffer, srcOffset, this.activeBuffer!, dstOffset, bytesToCopy);
                bytesLeft -= bytesToCopy;
                this.availableBufferSize -= bytesToCopy;
                this.length += bytesToCopy;

                srcOffset += bytesToCopy;
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            this.Write(buffer, offset, count);

            return Task.CompletedTask;
        }

#if PLAT_SPAN_OVERLOADS
        /// <inheritdoc />
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override int Read(Span<byte> buffer)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int srcOffset = 0;
            int bytesLeft = buffer.Length;
            while (bytesLeft > 0)
            {
                if (this.availableBufferSize == 0)
                {
                    this.AllocateBuffer(0);
                }

                int dstOffset = this.activeBuffer!.Length - this.availableBufferSize;
                int bytesToCopy = Math.Min(bytesLeft, this.availableBufferSize);
                buffer.Slice(srcOffset, bytesToCopy).CopyTo(new Memory<byte>(this.activeBuffer, dstOffset, bytesToCopy));
                bytesLeft -= bytesToCopy;
                this.availableBufferSize -= bytesToCopy;
                this.length += bytesToCopy;

                srcOffset += bytesToCopy;
            }

            return default;
        }

        /// <inheritdoc />
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            int srcOffset = 0;
            int bytesLeft = buffer.Length;
            while (bytesLeft > 0)
            {
                if (this.availableBufferSize == 0)
                {
                    this.AllocateBuffer(0);
                }

                int dstOffset = this.activeBuffer!.Length - this.availableBufferSize;
                int bytesToCopy = Math.Min(bytesLeft, this.availableBufferSize);
                buffer.Slice(srcOffset, bytesToCopy).CopyTo(new Span<byte>(this.activeBuffer, dstOffset, bytesToCopy));
                bytesLeft -= bytesToCopy;
                this.availableBufferSize -= bytesToCopy;
                this.length += bytesToCopy;

                srcOffset += bytesToCopy;
            }
        }

#endif
        private void AllocateBuffer(int sizeHint)
        {
            if (this.availableBufferSize == 0 || this.availableBufferSize < sizeHint)
            {
                if (this.activeBuffer != null)
                {
                    if (this.filledBuffers == null)
                    {
                        this.filledBuffers = new List<FilledBuffer>();
                    }

                    int bytesInBuffer = this.activeBuffer.Length - this.availableBufferSize;
                    this.filledBuffers.Add(new FilledBuffer(this.activeBuffer, bytesInBuffer));
                }

                this.activeBuffer = this.arrayPool.Rent(Math.Max(this.chunkSize, sizeHint));
                this.availableBufferSize = this.activeBuffer.Length;
            }
        }

        private struct FilledBuffer
        {
            internal readonly byte[] buffer;

            internal readonly int bytesInBuffer;

            internal FilledBuffer(byte[] buffer, int bytesInBuffer) : this()
            {
                this.buffer = buffer;
                this.bytesInBuffer = bytesInBuffer;
            }
        }
    }
}
