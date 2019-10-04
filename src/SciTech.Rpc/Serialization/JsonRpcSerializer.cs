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

using SciTech.Rpc.Logging;
using SciTech.Rpc.Serialization.Internal;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SciTech.Rpc.Serialization
{
    public class JsonRpcSerializer : RpcSerializerBase
    {
        internal static readonly ILog Logger = LogProvider.For<JsonRpcSerializer>();

        internal static readonly bool TraceEnabled = true;// Logger.IsTraceEnabled();
        
        private readonly JsonSerializerOptions? options;

        public JsonRpcSerializer(JsonSerializerOptions? options = null)
        {
            this.options = options ?? new JsonSerializerOptions { IgnoreNullValues = true };
        }

        public override IRpcSerializer<T> CreateTyped<T>()
        {
            return (IRpcSerializer<T>)this.CreateTyped(typeof(T), _ => new JsonRpcSerializer<T>(this.options));
        }

        public override object? Deserialize(ReadOnlySequence<byte> input, Type type)
        {
            if (TraceEnabled)
            {
                TraceDeserialize(input, type);
            }

            var readerOptions = new JsonReaderOptions();

            if (this.options != null)
            {
                readerOptions.AllowTrailingCommas = this.options.AllowTrailingCommas;
                readerOptions.CommentHandling = this.options.ReadCommentHandling;
                readerOptions.MaxDepth = this.options.MaxDepth;
            };

            var reader = new Utf8JsonReader(input, readerOptions);
            return JsonSerializer.Deserialize(ref reader, type, this.options);
        }

        public override void Serialize(BufferWriterStream bufferWriter, object? input, Type type)
        {
            if( TraceEnabled)
            {
                TraceSerialize(input, type);
            }

            var writerOptions = new JsonWriterOptions();

            if (this.options != null)
            {
                writerOptions.Encoder = this.options.Encoder;
                writerOptions.Indented = this.options.WriteIndented;
            };

            using var writer = new Utf8JsonWriter((IBufferWriter<byte>)bufferWriter, writerOptions);
            JsonSerializer.Serialize(writer, input, type, this.options);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void TraceDeserialize(ReadOnlySequence<byte> input, Type type)
        {
            var readerOptions = new JsonReaderOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            };

            string text = Encoding.UTF8.GetString(input.ToArray());
            var reader = new Utf8JsonReader(input, readerOptions);
            var value = JsonSerializer.Deserialize(ref reader, type, new JsonSerializerOptions { AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip });
            Logger.Trace("Json deserialized message '{SerializedText}' to '{Value}'", text, value);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void TraceSerialize(object? input, Type type)
        {
            var writerOptions = new JsonWriterOptions();
            writerOptions.Indented = true;

            var text = JsonSerializer.Serialize(input, type, new JsonSerializerOptions { WriteIndented = true });
            Logger.Trace("Json serialized message: {SerializedText}", text);
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
        private readonly JsonSerializerOptions? options;

        internal JsonRpcSerializer(JsonSerializerOptions? options)
        {
            this.options = options;
        }

        public T Deserialize(ReadOnlySequence<byte> input)
        {
            if (JsonRpcSerializer.TraceEnabled)
            {
                JsonRpcSerializer.TraceDeserialize(input, typeof(T));
            }

            var readerOptions = new JsonReaderOptions();

            if(this.options != null  )
            {
                readerOptions.AllowTrailingCommas = this.options.AllowTrailingCommas;
                readerOptions.CommentHandling = this.options.ReadCommentHandling;
                readerOptions.MaxDepth = this.options.MaxDepth;
            };

            var reader = new Utf8JsonReader(input, readerOptions);
            return JsonSerializer.Deserialize<T>(ref reader, this.options);
        }

        public void Serialize(BufferWriterStream bufferWriter, T input)
        {
            if (JsonRpcSerializer.TraceEnabled)
            {
                JsonRpcSerializer.TraceSerialize(input, typeof(T));
            }
            
            var writerOptions = new JsonWriterOptions();

            if( this.options != null )
            {
                writerOptions.Encoder = this.options.Encoder;
                writerOptions.Indented = this.options.WriteIndented;
            };

            using var writer = new Utf8JsonWriter((IBufferWriter<byte>)bufferWriter, writerOptions);
            JsonSerializer.Serialize(writer, input, this.options);
        }
    }
}
