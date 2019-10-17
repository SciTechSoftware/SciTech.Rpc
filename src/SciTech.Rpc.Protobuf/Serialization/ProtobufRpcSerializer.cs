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

using ProtoBuf.Meta;
using SciTech.Rpc.Serialization.Internal;
using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace SciTech.Rpc.Serialization
{
    public sealed class ProtobufRpcSerializer : RpcSerializerBase
    {
        public static readonly RuntimeTypeModel DefaultTypeModel = RuntimeTypeModel.Create().AddRpcTypes();

        private readonly RuntimeTypeModel typeModel;


        public ProtobufRpcSerializer() : this(null)
        {
        }

        public ProtobufRpcSerializer(RuntimeTypeModel? typeModel)
        {
            this.typeModel = typeModel ?? DefaultTypeModel;
        }

        public override IRpcSerializer<T> CreateTyped<T>()
        {
            return (IRpcSerializer<T>)this.CreateTyped(typeof(T), _ => new ProtobufSerializer<T>(this.typeModel));
        }

        public override object? Deserialize(ReadOnlySequence<byte> input, Type type)
        {
            var typedSerializer = (IRpcSerializer)this.CreateTyped(type, this.ReflectionCreateTyped);
            return typedSerializer.Deserialize(input, type);
        }


        public override void Serialize(BufferWriterStream bufferWriter, object? input, Type type)
        {
            var typedSerializer = (IRpcSerializer)this.CreateTyped(type, this.ReflectionCreateTyped);
            typedSerializer.Serialize(bufferWriter, input, type);
        }


        private IRpcSerializer ReflectionCreateTyped(Type type)
        {
            Console.WriteLine("Creating typed ProtobufSerializer:  " + type);
            var typed = (IRpcSerializer)Activator.CreateInstance(typeof(ProtobufSerializer<>).MakeGenericType(type), this.typeModel);
            Console.WriteLine("Created typed ProtobufSerializer:  " + type);

            return typed;
        }
    }

    public static class ProtobufSerializerExtensions
    {

        public static RuntimeTypeModel AddRpcTypes(this RuntimeTypeModel typeModel)
        {
            if (typeModel is null) throw new ArgumentNullException(nameof(typeModel));

            var mt = typeModel.Add(typeof(RpcServerConnectionInfo), true);
            //mt.AddSubType(10, typeof(TcpRpcServerConnectionInfo));
            mt.UseConstructor = false;

            //mt = typeModel.Add(typeof(TcpRpcServerConnectionInfo), true);
            //mt.UseConstructor = false;

            typeModel.Add(typeof(EventArgs), true);

            return typeModel;
        }

    }

    internal class ProtobufSerializer<T> : IRpcSerializer<T>, IRpcSerializer
    {
        private readonly RuntimeTypeModel typeModel;

        public ProtobufSerializer(RuntimeTypeModel typeModel)
        {
            this.typeModel = typeModel;
        }

        public T Deserialize(ReadOnlySequence<byte> input, [AllowNull]T value)
        {
            using var state = ProtoBuf.ProtoReader.State.Create(input, this.typeModel);
            return state.DeserializeRoot<T>(value);
        }


        public void Serialize(BufferWriterStream bufferWriter, [AllowNull]T input)
        {
            using var state = ProtoBuf.ProtoWriter.State.Create((IBufferWriter<byte>)bufferWriter, this.typeModel);
            state.SerializeRoot(input);
        }

        IRpcSerializer<TOther> IRpcSerializer.CreateTyped<TOther>()
        {
            if (!typeof(T).Equals(typeof(TOther)))
            {
                throw new NotSupportedException();
            }

            return (IRpcSerializer<TOther>)this;
        }

        object? IRpcSerializer.Deserialize(ReadOnlySequence<byte> input, Type type)
        {
            if (!typeof(T).Equals(type))
            {
                throw new ArgumentException("Types do not match", nameof(type));
            }

            return this.Deserialize(input, default);

        }

        void IRpcSerializer.Serialize(BufferWriterStream bufferWriter, object? input, Type type)
        {
            if (!typeof(T).Equals(type))
            {
                throw new ArgumentException("Types do not match", nameof(type));
            }

            if (input is T typedInput)
            {
                this.Serialize(bufferWriter, typedInput);
            }
            else if (input == null)
            {
                this.Serialize(bufferWriter, default);
            }
            else
            {
                throw new ArgumentException($"'{nameof(input)}' must be of type '{typeof(T)}'.");
            }
        }
    }
}
