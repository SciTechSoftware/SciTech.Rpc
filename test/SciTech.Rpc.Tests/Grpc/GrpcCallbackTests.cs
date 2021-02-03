using SciTech.Rpc.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Grpc
{
    public class GrpcCallbackTests : CallbackTests
    {
        public GrpcCallbackTests() : base(new ProtobufRpcSerializer(), RpcConnectionType.Grpc, true)
        {
        }
    }
}
