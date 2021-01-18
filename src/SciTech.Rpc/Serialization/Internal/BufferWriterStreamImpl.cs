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

namespace SciTech.Rpc.Serialization.Internal
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should have correct suffix")]
    public sealed class BufferWriterStreamImpl : BufferWriterStream
    {
        public BufferWriterStreamImpl(int chunkSize = 16384, ArrayPool<byte>? arrayPool = null)
            : base(chunkSize, arrayPool)
        {
        }

        public BufferWriterStreamImpl()
            : base(16384, null)
        {
        }


        public new void Reset() => base.Reset();
    }
}
