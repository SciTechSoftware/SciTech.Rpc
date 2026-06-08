using SciTech.Buffers;
using SciTech.Threading;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;

namespace SciTech.IO
{
    public static class StreamExtensions
    {
        public static Stream AsStream(this ReadOnlySequence<byte> sequence)
        {
            return new ReadOnlySequenceStream(sequence);

        }

        public static async IAsyncEnumerable<byte[]> EnumerateChunksAsync(this Stream s, int bufferSize)
        {
            long bytesLeft = s.Length;

            byte[] buffer = [];
            while (bytesLeft > 0)
            {
                int chunkSize = (int)Math.Min(bufferSize, bytesLeft);
                if (buffer.Length != chunkSize)
                {
                    buffer = new byte[chunkSize];
                }

                await s.ReadExactlyAsync(buffer).ContextFree();
                bytesLeft -= chunkSize;

                yield return buffer;
            }
        }
    }
}
