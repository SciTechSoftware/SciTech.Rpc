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
using System.Buffers;
using System.Runtime.InteropServices;

namespace SciTech.Buffers
{
    public struct OwnedArraySegment<T> : IDisposable, IEquatable<OwnedArraySegment<T>>
    {
        internal OwnedArraySegment(ArraySegment<T> segment, bool isRented)
        {
            this.Segment = segment;
            this.IsRented = isRented;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
        public T[]? Array => this.Segment.Array;

        public int Count => this.Segment.Count;

        public int Offset => this.Segment.Offset;

        public ArraySegment<T> Segment { get; }

        private bool IsRented { get; }

        public void Dispose()
        {
            if (this.IsRented)
            {
                ArrayPool<T>.Shared.Return(this.Segment.Array!);
            }
        }

        public override bool Equals(object? obj)
        {
            return obj is OwnedArraySegment<T> other && this.Equals(other);
        }

        public bool Equals(OwnedArraySegment<T> other)
        {
            return this.Segment == other.Segment && this.IsRented == other.IsRented;
        }

        public override int GetHashCode()
        {
            return this.Segment.GetHashCode() + this.IsRented.GetHashCode();
        }

        public static bool operator ==(OwnedArraySegment<T> left, OwnedArraySegment<T> right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(OwnedArraySegment<T> left, OwnedArraySegment<T> right)
        {
            return !(left == right);
        }
    }

    public static class OwnedArraySegment
    {
        public static OwnedArraySegment<T> Create<T>(ReadOnlyMemory<T> memory)
        {
            if (MemoryMarshal.TryGetArray(memory, out var segment))
            {
                return new OwnedArraySegment<T>(segment, false);
            }

            var buffer = ArrayPool<T>.Shared.Rent(memory.Length);
            memory.CopyTo(buffer);
            return new OwnedArraySegment<T>(new ArraySegment<T>(buffer, 0, memory.Length), true);
        }

        public static OwnedArraySegment<T> Create<T>(int size)
        {
            var buffer = ArrayPool<T>.Shared.Rent(size);
            return new OwnedArraySegment<T>(new ArraySegment<T>(buffer, 0, size), true);
        }
    }
}
