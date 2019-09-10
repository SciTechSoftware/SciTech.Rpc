using NUnit.Framework;
using SciTech.Rpc.Client;
using SciTech.Rpc.Server;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Grpc
{
    public class GrpcTimeoutTests : TimeoutTests
    {
        public GrpcTimeoutTests() : base(RpcConnectionType.Grpc)//, roundTripTimeout)
        {
        }
    }
}
