using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Grpc
{
    public class GrpcActivationTests : ActivationTests
    {
        public GrpcActivationTests() : base(RpcConnectionType.Grpc)
        {

        }
    }
}
