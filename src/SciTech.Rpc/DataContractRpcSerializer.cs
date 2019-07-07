using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;

namespace SciTech.Rpc
{
    public class DataContractRpcSerializer : IRpcSerializer
    {
        DataContractSerializerSettings? settings;

        public DataContractRpcSerializer(DataContractSerializerSettings? settings)
        {
            this.settings = settings;   
        }

        public DataContractRpcSerializer()
        {
        }

        public object FromStream(Type type, Stream input)
        {
            var serializer = settings != null ? new DataContractSerializer(type, settings) : new DataContractSerializer(type);
            using (var reader = XmlDictionaryReader.CreateBinaryReader(input, XmlDictionaryReaderQuotas.Max))
            {
                var value = serializer.ReadObject(reader);
                return value;
            }
        }

        //public byte[] ToBytes<T>(T input)
        //{
        //    var ms = new MemoryStream();
        //    using (var writer = XmlDictionaryWriter.CreateBinaryWriter(ms))
        //    {
        //        var serializer = new DataContractSerializer(typeof(T), settings);
        //        serializer.WriteObject(writer, input);
        //    }
        //    return ms.ToArray();

        //}

        public void ToStream(Stream stream, object? input)
        {
            if (input != null)
            {
                using (var writer = XmlDictionaryWriter.CreateBinaryWriter(stream))
                {
                    var type = input.GetType();
                    var serializer = settings != null ? new DataContractSerializer(type, settings) : new DataContractSerializer(type);
                    serializer.WriteObject(writer, input);
                }
            }
        }
    }
}
