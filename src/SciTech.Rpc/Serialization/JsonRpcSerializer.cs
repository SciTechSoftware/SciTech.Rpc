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

using Microsoft.Extensions.Logging;
using SciTech.Rpc.Serialization.Internal;
using System;
using System.Buffers;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SciTech.Rpc.Serialization
{
    public class JsonRpcSerializer : RpcSerializerBase
    {
        private readonly ILogger? logger;

        public JsonRpcSerializer(JsonSerializerOptions? options, ILogger? logger = null)
        {
            this.logger = logger;
            this.TraceEnabled = this.logger?.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Trace) ?? false;

            this.Options = options ?? new JsonSerializerOptions { IgnoreNullValues = true };
        }

        public JsonRpcSerializer(ILogger? logger = null)
            : this(null, logger )
        {
        }

        internal JsonSerializerOptions Options { get; }

        internal bool TraceEnabled { get; }

        public override IRpcSerializer<T> CreateTyped<T>()
        {
            return (IRpcSerializer<T>)this.CreateTyped(typeof(T), _ => new JsonRpcSerializer<T>(this));
        }

        public override object? Deserialize(ReadOnlySequence<byte> input, Type type)
        {
            if (this.TraceEnabled)
            {
                this.TraceDeserialize(input, type);
            }

            var readerOptions = new JsonReaderOptions();

            if (this.Options != null)
            {
                readerOptions.AllowTrailingCommas = this.Options.AllowTrailingCommas;
                readerOptions.CommentHandling = this.Options.ReadCommentHandling;
                readerOptions.MaxDepth = this.Options.MaxDepth;
            };

            var reader = new Utf8JsonReader(input, readerOptions);
            return JsonSerializer.Deserialize(ref reader, type, this.Options);
        }

        public override void Serialize(BufferWriterStream bufferWriter, object? input, Type type)
        {
            if (this.TraceEnabled)
            {
                this.TraceSerialize(input, type);
            }

            var writerOptions = new JsonWriterOptions();

            if (this.Options != null)
            {
                writerOptions.Encoder = this.Options.Encoder;
                writerOptions.Indented = this.Options.WriteIndented;
            };

            using var writer = new Utf8JsonWriter((IBufferWriter<byte>)bufferWriter, writerOptions);
            JsonSerializer.Serialize(writer, input, type, this.Options);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void TraceDeserialize(ReadOnlySequence<byte> input, Type type)
        {
            var readerOptions = new JsonReaderOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            };

            string text = Encoding.UTF8.GetString(input.ToArray());
            var reader = new Utf8JsonReader(input, readerOptions);
            var value = JsonSerializer.Deserialize(ref reader, type, new JsonSerializerOptions { AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip });
            this.logger?.LogTrace("Json deserialized message '{SerializedText}' to '{Value}'", text, value);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void TraceSerialize(object? input, Type type)
        {
            var writerOptions = new JsonWriterOptions();
            writerOptions.Indented = true;

            var text = JsonSerializer.Serialize(input, type, new JsonSerializerOptions { WriteIndented = true });
            this.logger?.LogTrace("Json serialized message: {SerializedText}", text);
        }
    }

    internal class RpcObjectIdJsonConverter : JsonConverter<RpcObjectId>
    {
        public override RpcObjectId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var id = reader.GetGuid();
            return new RpcObjectId(id);
        }

        public override void Write(Utf8JsonWriter writer, RpcObjectId value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Id);
        }
    }
    internal class RpcServerIdJsonConverter : JsonConverter<RpcServerId>
    {
        public override RpcServerId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var id = reader.GetGuid();
            return new RpcServerId(id);
        }

        public override void Write(Utf8JsonWriter writer, RpcServerId value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Id);
        }
    }

    //internal class KnownTypeJsonConverter : JsonConverter<object>
    //{
    //    private Dictionary<Type, KnownSerializationType> knownTypes = new Dictionary<Type, KnownSerializationType>();

    //    internal KnownTypeJsonConverter(List<KnownSerializationType> knownTypes)
    //    {
    //        foreach( var kt in knownTypes)
    //        {
    //            this.knownTypes.Add(kt.KnownType, kt);
    //        }
    //    }

    //    public override bool CanConvert(Type typeToConvert)
    //    {
    //        return typeToConvert.Name=="BaseClass";
    //    }

    //    public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    //    {

    //        throw new NotImplementedException();
    //    }

    //}

    internal class JsonRpcSerializer<T> : IRpcSerializer<T>
    {
        private readonly JsonRpcSerializer baseSerializer;

        internal JsonRpcSerializer(JsonRpcSerializer baseSerializer)
        {
            this.baseSerializer = baseSerializer;
        }

        public T Deserialize(ReadOnlySequence<byte> input, T value)
        {
            if (this.baseSerializer.TraceEnabled)
            {
                this.baseSerializer.TraceDeserialize(input, typeof(T));
            }

            var readerOptions = new JsonReaderOptions();

            var options = this.baseSerializer.Options;
            readerOptions.AllowTrailingCommas = options.AllowTrailingCommas;
            readerOptions.CommentHandling = options.ReadCommentHandling;
            readerOptions.MaxDepth = options.MaxDepth;

            var reader = new Utf8JsonReader(input, readerOptions);
            return JsonSerializer.Deserialize<T>(ref reader, options);
        }

        public void Serialize(BufferWriterStream bufferWriter, T input)
        {
            if (this.baseSerializer.TraceEnabled)
            {
                this.baseSerializer.TraceSerialize(input, typeof(T));
            }

            var writerOptions = new JsonWriterOptions();

            var options = this.baseSerializer.Options;
            writerOptions.Encoder = options.Encoder;
            writerOptions.Indented = options.WriteIndented;

            using var writer = new Utf8JsonWriter((IBufferWriter<byte>)bufferWriter, writerOptions);
            JsonSerializer.Serialize(writer, input, options);
        }
    }
}
