#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB.
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

namespace SciTech.Rpc.Serialization
{
    /// <summary>
    /// A combined writer <see cref="Stream"/> and <see cref="IBufferWriter{T}"/>. It is used
    /// by the <see cref="IRpcSerializer"/> and allows serializer implementations to decide whether
    /// it is more efficient to use <see cref="Stream"/> methods or <see cref="IBufferWriter{T}"/>
    /// methods when serializing an object.
    /// </summary>
    // TODO: If this class is kept, unit tests must be written.
    public class BufferWriterStream : Stream, IBufferWriter<byte>
    {
        private byte[]? activeBuffer;

        private ArrayPool<byte> arrayPool;

        private int availableBufferSize;

        private int chunkSize;

        private List<FilledBuffer>? filledBuffers;

        private long length;

        private protected BufferWriterStream(int chunkSize = 16384, ArrayPool<byte>? arrayPool = null)
        {
            this.arrayPool = arrayPool ?? ArrayPool<byte>.Shared;
            this.chunkSize = chunkSize;
        }

        public override sealed bool CanRead => false;

        public override sealed bool CanSeek => false;

        public override sealed bool CanWrite => true;

        public override sealed long Length => this.length;

        public override sealed long Position { get => this.length; set => throw new NotSupportedException(); }

        public void Advance(int count)
        {
            if (count < 0 || count > this.availableBufferSize)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            this.availableBufferSize -= count;
            this.length += count;
        }

        public override sealed void Close()
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
                    writer.Write(new ReadOnlySpan<byte>(this.activeBuffer, 0, bytesInBuffer));
                }
            }
        }



        public override sealed void Flush()
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

        public override sealed int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override sealed long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override sealed void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public byte[] ToArray()
        {
            var data = new byte[this.length];
            Span<byte> destData = data;

            if (this.filledBuffers != null)
            {
                foreach (var buffer in this.filledBuffers)
                {
                    var srcData = new ReadOnlySpan<byte>(buffer.buffer, 0, buffer.bytesInBuffer);
                    srcData.CopyTo(destData);
                    destData = destData.Slice(srcData.Length);
                }
            }

            if (this.activeBuffer != null)
            {
                int bytesInBuffer = this.activeBuffer.Length - this.availableBufferSize;
                if (bytesInBuffer > 0)
                {
                    var srcData = new ReadOnlySpan<byte>(this.activeBuffer, 0, bytesInBuffer);
                    srcData.CopyTo(destData);
                }
            }

            return data;
        }

        public override sealed void Write(byte[] buffer, int offset, int count)
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

        public override sealed Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            this.Write(buffer, offset, count);

            return Task.CompletedTask;
        }

        internal void Reset()
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

#if PLAT_SPAN_OVERLOADS
        /// <inheritdoc />
        public override sealed ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override sealed int Read(Span<byte> buffer)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override sealed ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
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
        public override sealed void Write(ReadOnlySpan<byte> buffer)
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
    }
}
