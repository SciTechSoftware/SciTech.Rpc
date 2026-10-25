#region Copyright notice and license
// Copyright (c) SciTech Software AB and contributors.
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
using System.Linq;

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

            // Known types are normally handled by AddRpcTypes, but the type model may have been created without it.
            ProtobufSerializerExtensions.AddKnownTypesHandler(this.typeModel);
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
            var typed = (IRpcSerializer)Activator.CreateInstance(typeof(ProtobufSerializer<>).MakeGenericType(type), this.typeModel)!;

            return typed;
        }
    }

    public static class ProtobufSerializerExtensions
    {

        /// <summary>
        /// Adds the types used by SciTech.Rpc to <paramref name="typeModel"/>, and makes the type model add the known types declared
        /// by <see cref="RpcKnownTypeAttribute"/> as sub-types.
        /// </summary>
        /// <remarks>Known types are only added to types that are added with default behaviour applied, i.e.
        /// added implicitly or using <c>Add(type, applyDefaultBehaviour: true)</c>.</remarks>
        public static RuntimeTypeModel AddRpcTypes(this RuntimeTypeModel typeModel)
        {
            if (typeModel is null) throw new ArgumentNullException(nameof(typeModel));

            AddKnownTypesHandler(typeModel);

            var mt = typeModel.Add(typeof(RpcConnectionInfo), true);
            mt.UseConstructor = false;

            typeModel.Add(typeof(EventArgs), true);

            return typeModel;
        }

        internal static void AddKnownTypesHandler(RuntimeTypeModel typeModel)
        {
            // Only applies to types added after this, so it should be called before any type is added.
            typeModel.AfterApplyDefaultBehaviour -= AddKnownSubTypes;
            typeModel.AfterApplyDefaultBehaviour += AddKnownSubTypes;
        }

        private static void AddKnownSubTypes(object? sender, TypeAddedEventArgs e)
        {
            var metaType = e.MetaType;
            if (metaType == null)
            {
                return;
            }

            var knownTypes = RpcKnownTypes.GetDeclaredKnownTypes(e.Type);
            if (knownTypes.Count == 0)
            {
                return;
            }

            var subTypes = metaType.GetSubtypes();
            foreach (var knownType in knownTypes)
            {
                // The sub-type may also have been declared using ProtoIncludeAttribute.
                if (!subTypes.Any(s => s.DerivedType.Type == knownType.KnownType))
                {
                    metaType.AddSubType(knownType.Order, knownType.KnownType);
                }
            }
        }

    }

    internal class ProtobufSerializer<T> : IRpcSerializer<T>, IRpcSerializer
    {
        private readonly RuntimeTypeModel typeModel;

        public ProtobufSerializer(RuntimeTypeModel typeModel)
        {
            this.typeModel = typeModel;
        }

        public T Deserialize(ReadOnlySequence<byte> input, [AllowNull] T value)
        {
            using var state = ProtoBuf.ProtoReader.State.Create(input, this.typeModel);
            return state.DeserializeRoot<T>(value!);
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
