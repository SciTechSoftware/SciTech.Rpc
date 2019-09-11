using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.NetGrpc
{
#if NETCOREAPP3_0
    public class NetGrpcAsyncSequenceTests : AsyncSequenceTests
    {
        public NetGrpcAsyncSequenceTests() : base(new ProtobufSerializer(), RpcConnectionType.NetGrpc, true)
        {
        }
    }
#endif
}
