using NUnit.Framework;
using SciTech.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Core
{
    [TestFixture]
    public class StreamExtensionsTests
    {
        [TestCase(10, 4, new[] { 4, 4, 2 })]
        [TestCase(8, 4, new[] { 4, 4 })]
        [TestCase(3, 4, new[] { 3 })]
        [TestCase(0, 4, new int[0])]
        public async Task EnumerateChunksAsync_Should_SplitStreamIntoChunks(int length, int chunkSize, int[] expectedChunkLengths)
        {
            var data = CreateData(length);
            using var stream = new MemoryStream(data);

            var chunks = await ToListAsync(stream.EnumerateChunksAsync(chunkSize));

            CollectionAssert.AreEqual(expectedChunkLengths, chunks.Select(c => c.Length));
            CollectionAssert.AreEqual(data, chunks.SelectMany(c => c));
        }

        [Test]
        public async Task EnumerateChunksAsync_Should_FillChunks_WhenStreamReturnsPartialReads()
        {
            var data = CreateData(10);
            using var stream = new TrickleStream(data, 3);

            var chunks = await ToListAsync(stream.EnumerateChunksAsync(4));

            CollectionAssert.AreEqual(new[] { 4, 4, 2 }, chunks.Select(c => c.Length));
            CollectionAssert.AreEqual(data, chunks.SelectMany(c => c));
        }

        [Test]
        public async Task EnumerateChunksAsync_Should_ReturnNewArrayForEachChunk()
        {
            using var stream = new MemoryStream(CreateData(12));

            var chunks = await ToListAsync(stream.EnumerateChunksAsync(4));

            Assert.AreEqual(3, chunks.Distinct().Count());
            CollectionAssert.AreEqual(new byte[] { 4, 5, 6, 7 }, chunks[1]);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task EnumerateChunksAsync_Should_DisposeStream_OnlyWhenTakingOwnership(bool takeOwnership)
        {
            var stream = new MemoryStream(CreateData(10));

            await ToListAsync(stream.EnumerateChunksAsync(4, takeOwnership));

            Assert.AreEqual(!takeOwnership, stream.CanRead);
        }

        [Test]
        public async Task EnumerateChunksAsync_Should_DisposeOwnedStream_WhenEnumerationIsStopped()
        {
            var stream = new MemoryStream(CreateData(10));

            await foreach (var _ in stream.EnumerateChunksAsync(4, true))
            {
                break;
            }

            Assert.IsFalse(stream.CanRead);
        }

        [Test]
        public void EnumerateChunksAsync_Should_ThrowAndDisposeOwnedStream_WhenCancelled()
        {
            var stream = new MemoryStream(CreateData(10));
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(() => ToListAsync(stream.EnumerateChunksAsync(4, true, cts.Token)));
            Assert.IsFalse(stream.CanRead);
        }

        [Test]
        public void EnumerateChunksAsync_Should_ValidateArguments_BeforeEnumeration()
        {
            Assert.Throws<ArgumentNullException>(() => StreamExtensions.EnumerateChunksAsync(null!, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryStream().EnumerateChunksAsync(0));
        }

        [Test]
        public async Task WriteAsync_Should_WriteAllChunks()
        {
            var data = CreateData(10);
            using var source = new MemoryStream(data);
            using var target = new MemoryStream();

            await target.WriteAsync(source.EnumerateChunksAsync(4));

            CollectionAssert.AreEqual(data, target.ToArray());
        }

        private static byte[] CreateData(int length) => Enumerable.Range(0, length).Select(i => (byte)i).ToArray();

        private static async Task<List<byte[]>> ToListAsync(IAsyncEnumerable<byte[]> chunks)
        {
            var list = new List<byte[]>();
            await foreach (var chunk in chunks)
            {
                list.Add(chunk);
            }

            return list;
        }

        /// <summary>
        /// Non-seekable stream that returns at most a fixed number of bytes from each read.
        /// </summary>
        private sealed class TrickleStream : Stream
        {
            private readonly MemoryStream inner;

            private readonly int maxReadSize;

            internal TrickleStream(byte[] data, int maxReadSize)
            {
                this.inner = new MemoryStream(data);
                this.maxReadSize = maxReadSize;
            }

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count) => this.inner.Read(buffer, offset, Math.Min(count, this.maxReadSize));

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    this.inner.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
