using SciTech.Rpc.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.NetGrpc
{
#if PLAT_NET_GRPC
    public class NetGrpcAsyncSequenceTests : AsyncSequenceTests
    {
        public NetGrpcAsyncSequenceTests() : base(new ProtobufRpcSerializer(), RpcConnectionType.NetGrpc, true)
        {
        }
    }
#endif
}
