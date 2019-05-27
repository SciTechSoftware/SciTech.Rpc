using SciTech.IO;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc
{
    public interface IRpcSerializer
    {
        object FromStream(Type type, Stream input);

        void ToStream(Stream stream, object? input );
    }


#pragma warning disable CA1062 // Validate arguments of public methods
    public static class RpcSerializerExtensions
    {
        public static T FromStream<T>(this IRpcSerializer serializer, Stream input)
        {
            return (T)serializer.FromStream(typeof(T), input);
        }

        //public static void ToStream(this IRpcStreamSerializer serializer, Stream stream, object input )
        //{
        //    serializer.ToStream(typeof(T), stream, input);
        //}

        public static T FromBytes<T>(this IRpcSerializer serializer, byte[] input)
        {
            using (var ms = new MemoryStream(input))
            {
                return FromStream<T>(serializer, ms);
            }
        }
        public static object FromBytes(this IRpcSerializer serializer, Type type, byte[] input)
        {
            using (var ms = new MemoryStream(input))
            {
                return serializer.FromStream(type, ms);
            }
        }

        public static byte[] ToBytes<T>(this IRpcSerializer serializer, T input)
        {
            using (var ms = new MemoryStream())
            {
                serializer.ToStream(ms, input);
                return ms.ToArray();
            }
        }
    }
#pragma warning restore CA1062 // Validate arguments of public methods

    public interface IRpcAsyncStreamSerializer
    {
        T FromStream<T>(Stream input);

        void ToStream<T>(Stream destination, T input);
    }


    public interface IRpcSerializer<TRequestPayLoad, TResponsePayload>
    {
        T FromBytes<T>(TRequestPayLoad input);

        TResponsePayload ToBytes<T>(T input);
    }
}
