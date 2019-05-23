#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//

// Based on Nerdbank.Streams.PipeStream (https://github.com/AArnott/Nerdbank.Streams)
//
// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.
#endregion

using SciTech.Threading;
using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.IO
{

    /// <summary>
    /// Wraps a <see cref="IBufferWriter{T}"/> as a <see cref="Stream"/> for
    /// easier interop with existing APIs. It also counts the number of bytes written to the stream
    /// since the creation. The number of bytes written can be retrieved using <see cref="Length"/> or <see cref="Position"/>.
    /// </summary>
    public class CountedBufferWriterStream : Stream
    {
        /// <summary>
        /// The <see cref="PipeWriter"/> to use when writing to this stream. 
        /// </summary>
        private readonly IBufferWriter<byte> writer;

        long bytesWritten;

        /// <summary>
        /// Initializes a new instance of the <see cref="PipeStream"/> class.
        /// </summary>
        /// <param name="writer">The <see cref="PipeWriter"/> to use when writing to this stream. May be null.</param>
        public CountedBufferWriterStream(IBufferWriter<byte> writer)
        {
            this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        /// <inheritdoc />
        public override bool CanRead => false;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => !this.IsDisposed && this.writer != null;

        /// <inheritdoc />
        public bool IsDisposed { get; private set; }

        /// <inheritdoc />
        public override long Length => this.bytesWritten;

        /// <inheritdoc />
        public override long Position
        {
            get => this.bytesWritten;
            set => throw this.ThrowDisposedOr(new NotSupportedException());
        }

        /// <inheritdoc />
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            // Do nothing.
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc />
        public override void SetLength(long value) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc />
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            if (this.IsDisposed) throw new ObjectDisposedException(this.ToString());

            cancellationToken.ThrowIfCancellationRequested();

            this.writer.Write(buffer.AsSpan().Slice(offset, count));
            this.bytesWritten += buffer.Length;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            this.IsDisposed = true;
            base.Dispose(disposing);
        }


        private Exception ThrowDisposedOr(Exception ex)
        {
            if (this.IsDisposed)
            {
                throw new ObjectDisposedException(this.ToString());
            }

            throw ex;
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

            if (this.IsDisposed) throw new ObjectDisposedException(this.ToString());

            this.writer.Write(buffer.Span);
            this.bytesWritten += buffer.Length;
            return default;
        }

        /// <inheritdoc />
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            this.writer.Write(buffer);
            this.bytesWritten += buffer.Length;
        }

#endif

        /// <inheritdoc />
        public override void Flush()
        {
            // Do nothing.
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (this.IsDisposed) throw new ObjectDisposedException(this.ToString());

            this.writer.Write(buffer.AsSpan().Slice(offset, count));
            this.bytesWritten += count;
        }
    }
}
