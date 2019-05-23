using ProtoBuf.Meta;
using System;
using System.IO;

namespace SciTech.Rpc
{
    public class ProtobufSerializer : IRpcSerializer
    {
        public static readonly RuntimeTypeModel DefaultTypeModel = TypeModel.Create().AddRpcTypes();

        private readonly RuntimeTypeModel typeModel;

        public ProtobufSerializer() : this(null)
        {
        }

        public ProtobufSerializer(RuntimeTypeModel? typeModel)
        {
            this.typeModel = typeModel ?? DefaultTypeModel;
        }

        public object FromStream(Type type, Stream input)
        {
            return this.typeModel.Deserialize(input, null, type);
        }
        //public void ToStream<T>(Stream stream, T input)
        //{
        //    this.typeModel.Serialize(stream, input);
        //}

        public void ToStream(Stream stream, object? input)
        {
            this.typeModel.Serialize(stream, input);
        }
    }

    //public class ProtobufSerializer : IRpcSerializer
    //{
    //    public static readonly RuntimeTypeModel DefaultTypeModel = TypeModel.Create().AddRpcTypes();
    //    private readonly RuntimeTypeModel typeModel;
    //    public ProtobufSerializer() : this(null)
    //    {
    //    }
    //    public ProtobufSerializer(RuntimeTypeModel typeModel)
    //    {
    //        this.typeModel = typeModel ?? ProtobufSerializer.DefaultTypeModel;
    //    }
    //    public T FromBytes<T>(byte[] input)
    //    {
    //        using (var stream = new MemoryStream(input))
    //        {
    //            if (typeof(RpcObjectRef).IsAssignableFrom(typeof(T)))
    //            {
    //                return (T)this.typeModel.Deserialize(stream, null, typeof(T));
    //                //return (typeModel.Deserialize(stream, null, typeof(RpcObjectRef)) as RpcObjectRef);
    //            }
    //            else
    //            {
    //                return (T)this.typeModel.Deserialize(stream, null, typeof(T));
    //            }
    //        }
    //    }
    //    public byte[] ToBytes<T>(T input)
    //    {
    //        using (var stream = new MemoryStream())
    //        {
    //            if (input is RpcObjectRef objectRef)
    //            {
    //                this.typeModel.Serialize(stream, objectRef);
    //            }
    //            else
    //            {
    //                this.typeModel.Serialize(stream, input);
    //            }
    //            return stream.ToArray();
    //        }
    //    }
    //}
    //public class ProtobufSequenceSerializer : IRpcSerializer<ReadOnlySequence<byte>, ReadOnlySequence<byte>>
    //{
    //    public static readonly RuntimeTypeModel DefaultTypeModel = TypeModel.Create().AddRpcTypes();
    //    private readonly RuntimeTypeModel typeModel;
    //    public ProtobufSequenceSerializer() : this(null)
    //    {
    //    }
    //    public ProtobufSequenceSerializer(RuntimeTypeModel typeModel)
    //    {
    //        this.typeModel = typeModel ?? ProtobufSerializer.DefaultTypeModel;
    //        var reader = ProtoReader.Create(null, this.typeModel, null);
    //        reader.ReadFieldHeader();
    //    }
    //    public T FromBytes<T>(ReadOnlySequence<byte> input)
    //    {
    //        using (var stream = input.AsStream())
    //        {
    //            return (T)this.typeModel.Deserialize(stream, null, typeof(T));
    //        }
    //    }
    //    public ReadOnlySequence<byte> ToBytes<T>(T input)
    //    {
    //        using (var stream = new MemoryStream())
    //        {
    //            if (input is RpcObjectRef objectRef)
    //            {
    //                this.typeModel.Serialize(stream, objectRef);
    //            }
    //            else
    //            {
    //                this.typeModel.Serialize(stream, input);
    //            }
    //            return stream.ToArray();
    //        }
    //    }
    //}
    public static class ProtobufSerializerExtensions
    {
        public static RuntimeTypeModel AddRpcTypes(this RuntimeTypeModel typeModel)
        {
            var mt = typeModel.Add(typeof(RpcServerConnectionInfo), true);
            //mt.AddSubType(10, typeof(TcpRpcServerConnectionInfo));
            mt.UseConstructor = false;

            //mt = typeModel.Add(typeof(TcpRpcServerConnectionInfo), true);
            //mt.UseConstructor = false;

            typeModel.Add(typeof(EventArgs), true);

            return typeModel;
        }
    }
}
