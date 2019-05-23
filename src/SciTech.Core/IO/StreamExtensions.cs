using SciTech.Buffers;
using System;
using System.Buffers;
using System.IO;

namespace SciTech.IO
{
    public static class StreamExtensions
    {
        public static Stream AsStream(this ReadOnlySequence<byte> sequence)
        {
            return new ReadOnlySequenceStream(sequence);

        }
    }
}
