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

using SciTech.Rpc.Serialization.Internal;
using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace SciTech.Rpc.Serialization
{
    public static class RpcSerializerExtensions
    {
        [return: MaybeNull]
        public static T Deserialize<T>(this IRpcSerializer<T> serializer, byte[]? input)
        {
            if (serializer is null) throw new ArgumentNullException(nameof(serializer));

            return input != null ? serializer.Deserialize(new ReadOnlySequence<byte>(input)) : default;
        }

        public static object? Deserialize(this IRpcSerializer serializer, byte[]? input, Type type)
        {
            if (serializer is null) throw new ArgumentNullException(nameof(serializer));

            return input != null ? serializer.Deserialize(new ReadOnlySequence<byte>(input), type) : default;
        }

        public static T? Deserialize<T>(this IRpcSerializer serializer, byte[]? input)
            where T : class
        {
            if (serializer is null) throw new ArgumentNullException(nameof(serializer));

            return input != null ? (T?)serializer.Deserialize(new ReadOnlySequence<byte>(input), typeof(T)) : null;
        }

        public static T? Deserialize<T>(this IRpcSerializer serializer, ReadOnlySequence<byte> input)
            where T : class
        {
            if (serializer is null) throw new ArgumentNullException(nameof(serializer));

            return (T?)serializer.Deserialize(input, typeof(T));
        }

        public static byte[]? Serialize<T>(this IRpcSerializer<T> serializer, [AllowNull]T input)
        {
            if (serializer is null) throw new ArgumentNullException(nameof(serializer));

            using var ms = new BufferWriterStreamImpl();
            serializer.Serialize(ms, input);
            return ms.ToArray();
        }

        [return:NotNullIfNotNull("input")]
        public static byte[]? Serialize<T>(this IRpcSerializer serializer, [AllowNull]T input)
        {
            if (serializer is null) throw new ArgumentNullException(nameof(serializer));

            using var ms = new BufferWriterStreamImpl();
            serializer.Serialize(ms, input, typeof(T));
            return ms.ToArray();
        }

        public static byte[] Serialize(this IRpcSerializer serializer, object? input, Type type)
        {
            if (serializer is null) throw new ArgumentNullException(nameof(serializer));

            using var ms = new BufferWriterStreamImpl();
            serializer.Serialize(ms, input, type);
            return ms.ToArray();
        }
    }
}
