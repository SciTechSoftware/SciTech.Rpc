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
using System.Diagnostics.CodeAnalysis;

namespace SciTech.Rpc.Serialization
{
    public interface IRpcSerializer
    {
        IRpcSerializer<T> CreateTyped<T>();

        object? Deserialize(ReadOnlySequence<byte> input, Type type);

        void Serialize(BufferWriterStream bufferWriter, object? input, Type type);
    }


    public interface IRpcSerializer<T>
    {
        T Deserialize(ReadOnlySequence<byte> input, [AllowNull]T value=default);

        void Serialize(BufferWriterStream bufferWriter, T input);
    }
}
