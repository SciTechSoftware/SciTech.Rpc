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

using SciTech.IO;
using SciTech.Rpc.Logging;
using SciTech.Rpc.Serialization.Internal;
using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;

namespace SciTech.Rpc.Serialization
{
    public class DataContractRpcSerializer : RpcSerializerBase
    {
        [SuppressMessage("Performance", "CA1802:Use literals where appropriate", Justification = "Logging temporarily removed.")]
        private static readonly bool TraceEnabled = false; // TODO: Logger.IsTraceEnabled();

        private readonly DataContractSerializerSettings? settings;
        private readonly ISerializationSurrogateProvider? surrogateProvider;

        public DataContractRpcSerializer()
        {
        }

        public DataContractRpcSerializer(DataContractSerializerSettings? settings)
        {
            this.settings = settings;
        }
        
        public DataContractRpcSerializer(DataContractSerializerSettings? settings, ISerializationSurrogateProvider? surrogateProvider)
        {
            this.settings = settings;
            this.surrogateProvider = surrogateProvider;
        }

        

        public override IRpcSerializer<T> CreateTyped<T>()
        {
            return (IRpcSerializer<T>)this.CreateTyped(typeof(T), _ => new DataContractRpcSerializer<T>(this.settings, this.surrogateProvider));
        }

        public override object? Deserialize(ReadOnlySequence<byte> input, Type type)
        {
            var serializer = this.settings != null ? new DataContractSerializer(type, this.settings) : new DataContractSerializer(type);
            return Deserialize(input, serializer);
        }

        public override void Serialize(BufferWriterStream bufferWriter, object? input, Type type)
        {
            var serializer = this.settings != null ? new DataContractSerializer(type, this.settings) : new DataContractSerializer(type);

            Serialize(bufferWriter, input, serializer);
        }

        internal static object? Deserialize(ReadOnlySequence<byte> input, DataContractSerializer serializer)
        {
            using var stream = input.AsStream();
            using (var reader = XmlDictionaryReader.CreateBinaryReader(stream, XmlDictionaryReaderQuotas.Max))
            {
                return serializer.ReadObject(reader);
            }
        }

        internal static void Serialize(BufferWriterStream bufferWriter, object? input, DataContractSerializer serializer)
        {
            if (TraceEnabled)
            {
                TraceSerialize(input, serializer);
            }

            using var writer = XmlDictionaryWriter.CreateBinaryWriter(bufferWriter, null, null, false);
            serializer.WriteObject(writer, input);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void TraceSerialize(object? input, DataContractSerializer serializer)
        {
            var memStream = new MemoryStream();
            using (var traceWriter = XmlDictionaryWriter.CreateTextWriter(memStream, Encoding.UTF8, false))
            {
                serializer.WriteObject(traceWriter, input);
            }

            var bytes = memStream.ToArray();

            string text = Encoding.UTF8.GetString(bytes);
            // TODO: Logger.Trace("ToStream: {SerializedText}", text);
        }
    }

    internal class DataContractRpcSerializer<T> : IRpcSerializer<T>
    {
        private readonly DataContractSerializer dataContractSerializer;

        public DataContractRpcSerializer(DataContractSerializerSettings? settings, ISerializationSurrogateProvider? surrogateProvider)
        {
            this.dataContractSerializer = settings != null ? new DataContractSerializer(typeof(T), settings) : new DataContractSerializer(typeof(T));
            if( surrogateProvider != null )
            {
                this.dataContractSerializer.SetSerializationSurrogateProvider(surrogateProvider);
            }
        }

        [return: MaybeNull]
        public T Deserialize(ReadOnlySequence<byte> input, [AllowNull]T value)
        {
            return (T)DataContractRpcSerializer.Deserialize(input, this.dataContractSerializer)!;
        }

        public void Serialize(BufferWriterStream bufferWriter, [AllowNull]T input)
        {
            DataContractRpcSerializer.Serialize(bufferWriter, input, this.dataContractSerializer);
        }
    }
}
