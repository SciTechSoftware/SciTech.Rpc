using SciTech.Rpc.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Grpc
{
    public class GrpcAsyncSequenceTests : AsyncSequenceTests
    {
        public GrpcAsyncSequenceTests() : base(new ProtobufRpcSerializer(), RpcConnectionType.Grpc, true)
        {
        }
    }
}
