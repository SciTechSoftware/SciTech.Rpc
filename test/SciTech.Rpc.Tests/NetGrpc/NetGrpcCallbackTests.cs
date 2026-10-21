using SciTech.Rpc.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.NetGrpc
{
#if NET
    public class NetGrpcCallbackTests : CallbackTests
    {
        public NetGrpcCallbackTests() : base(new ProtobufRpcSerializer(), RpcConnectionType.NetGrpc, true)
        {
        }
    }

#endif
}
