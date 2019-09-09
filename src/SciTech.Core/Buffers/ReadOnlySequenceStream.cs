#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//

// Based on Nerdbank.Streams.ReadOnlySequenceStream (https://github.com/AArnott/Nerdbank.Streams)
//
// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.
#endregion

using System;

namespace SciTech.Buffers
{
    using SciTech.Threading;
    using System;
    using System.Buffers;
    using System.IO;
    using System.IO.Pipelines;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;

    internal class ReadOnlySequenceStream : Stream
    {
        private static readonly Task<int> TaskOfZero = Task.FromResult(0);

        /// <summary>
        /// A reusable task if two consecutive reads return the same number of bytes.
        /// </summary>
        private Task<int>? lastReadTask;

        private SequencePosition position;

        private ReadOnlySequence<byte> readOnlySequence;

        public ReadOnlySequenceStream(ReadOnlySequence<byte> readOnlySequence)
        {
            this.readOnlySequence = readOnlySequence;
            this.position = readOnlySequence.Start;
        }

        /// <inheritdoc/>
        public override bool CanRead => !this.IsDisposed;

        /// <inheritdoc/>
        public override bool CanSeek => !this.IsDisposed;

        /// <inheritdoc/>
        public override bool CanWrite => false;

        /// <inheritdoc/>
        public bool IsDisposed { get; private set; }

        /// <inheritdoc/>
        public override long Length => this.ReturnOrThrowDisposed(this.readOnlySequence.Length);

        /// <inheritdoc/>
        public override long Position
        {
            get => this.readOnlySequence.Slice(0, this.position).Length;
            set
            {
                if (value < 0)
                {
                    throw new ArgumentException("Position must be >= 0", nameof(value));
                }

                this.position = this.readOnlySequence.GetPosition(value, this.readOnlySequence.Start);
            }
        }

        /// <inheritdoc/>
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            foreach (var segment in this.readOnlySequence)
            {
#if PLAT_SPAN_OVERLOADS
                await destination.WriteAsync(segment, cancellationToken).ConfigureAwait(false);
#else
                using (var blob = OwnedArraySegment.Create(segment))
                {
                    await destination.WriteAsync(blob.Array, blob.Offset, blob.Count).ContextFree();
                }
#endif
            }
        }


        /// <inheritdoc/>
        public override void Flush() => this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override Task FlushAsync(CancellationToken cancellationToken) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadOnlySequence<byte> remaining = this.readOnlySequence.Slice(this.position);
            ReadOnlySequence<byte> toCopy = remaining.Slice(0, Math.Min(count, remaining.Length));
            this.position = toCopy.End;
            toCopy.CopyTo(buffer.AsSpan(offset, count));
            return (int)toCopy.Length;
        }

        /// <inheritdoc/>
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int bytesRead = this.Read(buffer, offset, count);
            if (bytesRead == 0)
            {
                return TaskOfZero;
            }

            if (this.lastReadTask?.Result == bytesRead)
            {
                return this.lastReadTask;
            }
            else
            {
                return this.lastReadTask = Task.FromResult(bytesRead);
            }
        }

        /// <inheritdoc/>
        public override int ReadByte()
        {
            ReadOnlySequence<byte> remaining = this.readOnlySequence.Slice(this.position);
            if (remaining.Length > 0)
            {
                byte result = remaining.First.Span[0];
                this.position = this.readOnlySequence.GetPosition(1, this.position);
                return result;
            }
            else
            {
                return -1;
            }
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin)
        {
            if (this.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(ReadOnlySequenceStream));
            }

            SequencePosition relativeTo;
            switch (origin)
            {
                case SeekOrigin.Begin:
                    relativeTo = this.readOnlySequence.Start;
                    break;
                case SeekOrigin.Current:
                    if (offset >= 0)
                    {
                        relativeTo = this.position;
                    }
                    else
                    {
                        relativeTo = this.readOnlySequence.Start;
                        offset += this.Position;
                    }

                    break;
                case SeekOrigin.End:
                    if (offset >= 0)
                    {
                        relativeTo = this.readOnlySequence.End;
                    }
                    else
                    {
                        relativeTo = this.readOnlySequence.Start;
                        offset += this.Position;
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(origin));
            }

            this.position = this.readOnlySequence.GetPosition(offset, relativeTo);
            return this.Position;
        }

        /// <inheritdoc/>
        public override void SetLength(long value) => this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override void WriteByte(byte value) => this.ThrowDisposedOr(new NotSupportedException());

#if PLAT_SPAN_OVERLOADS
        /// <inheritdoc/>
        public override int Read(Span<byte> buffer)
        {
            ReadOnlySequence<byte> remaining = this.readOnlySequence.Slice(this.position);
            ReadOnlySequence<byte> toCopy = remaining.Slice(0, Math.Min(buffer.Length, remaining.Length));
            this.position = toCopy.End;
            toCopy.CopyTo(buffer);
            return (int)toCopy.Length;
        }

        /// <inheritdoc/>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<int>(this.Read(buffer.Span));
        }

        /// <inheritdoc/>
        public override void Write(ReadOnlySpan<byte> buffer) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc/>
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw this.ThrowDisposedOr(new NotSupportedException());

#endif

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            this.IsDisposed = true;
            base.Dispose(disposing);
        }

        private T ReturnOrThrowDisposed<T>(T value)
        {
            if (this.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(ReadOnlySequenceStream));
            }

            return value;
        }

        private Exception ThrowDisposedOr(Exception ex)
        {
            if (this.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(ReadOnlySequenceStream));
            }

            throw ex;
        }
    }
}
