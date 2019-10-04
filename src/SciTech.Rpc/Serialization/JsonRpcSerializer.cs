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

using SciTech.Rpc.Serialization.Internal;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text.Json;

namespace SciTech.Rpc.Serialization
{
    public class JsonRpcSerializer : RpcSerializerBase
    {
        private readonly Dictionary<Type, WeakReference> cachedSerializers = new Dictionary<Type, WeakReference>();

        private readonly JsonSerializerOptions options;

        private readonly object syncRoot = new object();

        public JsonRpcSerializer(JsonSerializerOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public override IRpcSerializer<T> CreateTyped<T>()
        {
            return (IRpcSerializer<T>)this.CreateTyped(typeof(T), _ => new JsonRpcSerializer(this.options));
        }

        public IRpcSerializer CreateTyped(Type type)
        {
            lock (this.syncRoot)
            {
                if (this.cachedSerializers.TryGetValue(type, out var wrSerializer) && wrSerializer.Target is IRpcSerializer serializer)
                {
                    return serializer;
                }
            }

            var newSerializer = (IRpcSerializer)Activator.CreateInstance(typeof(JsonRpcSerializer<>).MakeGenericType(type), this.options);
            lock (this.syncRoot)
            {
                this.cachedSerializers[type] = new WeakReference(newSerializer);
            }

            return newSerializer;
        }

        public override object? Deserialize(ReadOnlySequence<byte> input, Type type)
        {
            var readerOptions = new JsonReaderOptions
            {
                AllowTrailingCommas = this.options.AllowTrailingCommas,
                CommentHandling = this.options.ReadCommentHandling,
                MaxDepth = this.options.MaxDepth
            };

            var reader = new Utf8JsonReader(input, readerOptions);
            return JsonSerializer.Deserialize(ref reader, type, this.options);
        }

        public override void Serialize(BufferWriterStream bufferWriter, object? input, Type type)
        {
            var writerOptions = new JsonWriterOptions
            {
                Encoder = this.options.Encoder,
                Indented = this.options.WriteIndented
            };

            using var writer = new Utf8JsonWriter((IBufferWriter<byte>)bufferWriter, writerOptions);
            JsonSerializer.Serialize(writer, input, this.options);
        }
    }

    internal class JsonRpcSerializer<T> : IRpcSerializer<T>
    {
        private readonly JsonSerializerOptions options;

        internal JsonRpcSerializer(JsonSerializerOptions options)
        {
            this.options = options;
        }

        public T Deserialize(ReadOnlySequence<byte> input)
        {
            var readerOptions = new JsonReaderOptions
            {
                AllowTrailingCommas = this.options.AllowTrailingCommas,
                CommentHandling = this.options.ReadCommentHandling,
                MaxDepth = this.options.MaxDepth
            };

            var reader = new Utf8JsonReader(input, readerOptions);
            return JsonSerializer.Deserialize<T>(ref reader, this.options);
        }

        public void Serialize(BufferWriterStream bufferWriter, T input)
        {
            var writerOptions = new JsonWriterOptions
            {
                Encoder = this.options.Encoder,
                Indented = this.options.WriteIndented
            };

            using var writer = new Utf8JsonWriter((IBufferWriter<byte>)bufferWriter, writerOptions);
            JsonSerializer.Serialize(writer, input, this.options);
        }
    }
}
