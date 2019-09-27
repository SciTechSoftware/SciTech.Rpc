using SciTech.Rpc.Logging;
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
        private static readonly ILog Logger = LogProvider.For<DataContractRpcSerializer>();
        
        private static bool traceEnabled = false;   // TODO:

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
                var type = input.GetType();
                var serializer = settings != null ? new DataContractSerializer(type, settings) : new DataContractSerializer(type);

                if (traceEnabled)
                {
                    using var memStream = new MemoryStream();
                    using (var traceWriter = XmlDictionaryWriter.CreateTextWriter(memStream, Encoding.UTF8, false))
                    {
                        serializer.WriteObject(traceWriter, input);
                    }

                    var bytes = memStream.ToArray();
                    string text = Encoding.UTF8.GetString(bytes);
                    Logger.Trace("ToStream: {SerializedText}", text);
                }

                using var writer = XmlDictionaryWriter.CreateBinaryWriter(stream, null, null, false);

                serializer.WriteObject(writer, input);
            } else if( traceEnabled )
            {
                // Isn't this an error?
                Logger.Info("ToStream with null input");
            }
        }
    }
}
