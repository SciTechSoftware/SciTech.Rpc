using NUnit.Framework;
using SciTech.Rpc.Client;
using SciTech.Rpc.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests
{
    public class StreamingCallTests : ClientServerTestsBase
    {
        protected StreamingCallTests(RpcConnectionType connectionType) : base( new JsonRpcSerializer(), connectionType)
        {
        }

    }

}
