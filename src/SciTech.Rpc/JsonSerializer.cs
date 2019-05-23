using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace SciTech.Rpc.Grpc
{
    public class JsonSerializer : IGrpcSerializer
    {
        public T FromBytes<T>(byte[] input)
        {
            var text = Encoding.UTF8.GetString(input);
            return JsonConvert.DeserializeObject<T>(text);
        }

        public byte[] ToBytes<T>(T input)
        {
            var text = JsonConvert.SerializeObject(input);
            return Encoding.UTF8.GetBytes(text);
        }
    }
}
