using SciTech.Buffers;
using SciTech.Threading;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.IO
{
    public static class StreamExtensions
    {
        public static Stream AsStream(this ReadOnlySequence<byte> sequence)
        {
            return new ReadOnlySequenceStream(sequence);
        }

        /// <summary>
        /// Writes all chunks provided by <paramref name="enumerableData"/> to the stream.
        /// </summary>
        public static async Task WriteAsync(this Stream s, IAsyncEnumerable<byte[]> enumerableData, CancellationToken cancellationToken = default)
        {
            if (s is null) throw new ArgumentNullException(nameof(s));
            if (enumerableData is null) throw new ArgumentNullException(nameof(enumerableData));

            await foreach (var item in enumerableData.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                await s.WriteAsync(item, 0, item.Length, cancellationToken).ContextFree();
            }
        }

        /// <summary>
        /// Reads the stream from its current position to the end, as a sequence of chunks.
        /// </summary>
        /// <remarks>
        /// Each chunk is a newly allocated array, so chunks can be kept or queued by the consumer. All chunks except
        /// the last one have the length <paramref name="chunkSize"/>. The stream does not need to be seekable.
        /// <para>If <paramref name="takeOwnership"/> is <c>true</c>, the stream is disposed when the enumeration
        /// completes, fails or is disposed. Note that the stream will not be disposed if the enumeration is never started.</para>
        /// </remarks>
        /// <param name="s">The stream to read.</param>
        /// <param name="chunkSize">Maximum size of each chunk.</param>
        /// <param name="takeOwnership"><c>true</c> if the stream should be disposed when the enumeration ends.</param>
        /// <param name="cancellationToken">Cancellation token, combined with any token provided to <c>WithCancellation</c>.</param>
        public static IAsyncEnumerable<byte[]> EnumerateChunksAsync(this Stream s, int chunkSize, bool takeOwnership = false, CancellationToken cancellationToken = default)
        {
            // Validate arguments eagerly, an async iterator would not validate until the enumeration is started.
            if (s is null) throw new ArgumentNullException(nameof(s));
            if (chunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSize));

            return EnumerateChunksCoreAsync(s, chunkSize, takeOwnership, cancellationToken);
        }

        private static async IAsyncEnumerable<byte[]> EnumerateChunksCoreAsync(Stream s, int chunkSize, bool takeOwnership, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            try
            {
                while (true)
                {
                    var buffer = new byte[chunkSize];
                    int bytesRead = await ReadAtMostAsync(s, buffer, cancellationToken).ContextFree();
                    if (bytesRead == 0)
                    {
                        yield break;
                    }

                    if (bytesRead < chunkSize)
                    {
                        // End of stream reached.
                        Array.Resize(ref buffer, bytesRead);
                        yield return buffer;
                        yield break;
                    }

                    yield return buffer;
                }
            }
            finally
            {
                if (takeOwnership)
                {
                    s.Dispose();
                }
            }
        }

        /// <summary>
        /// Reads until <paramref name="buffer"/> is filled or the end of the stream is reached.
        /// </summary>
        /// <returns>The number of bytes read, less than the buffer length only if the end of the stream has been reached.</returns>
        private static async Task<int> ReadAtMostAsync(Stream s, byte[] buffer, CancellationToken cancellationToken)
        {
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int bytesRead = await s.ReadAsync(buffer, totalRead, buffer.Length - totalRead, cancellationToken).ContextFree();
                if (bytesRead == 0)
                {
                    break;
                }

                totalRead += bytesRead;
            }

            return totalRead;
        }
    }
}
