using SciTech.Rpc.Lightweight.Server;
using SciTech.Rpc.Server;
using SciTech.Rpc.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SciTech.Rpc.Lightweight.Client;
using SciTech.Rpc.Client;
using NUnit.Framework;

namespace SciTech.Rpc.Tests
{
    public class ProxyEqualityTests : ClientServerTestsBase
    {
        public ProxyEqualityTests() : base(new ProtobufRpcSerializer(), RpcConnectionType.LightweightNamedPipe)
        {
        }

        [Test]
        public void Test()
        {
            using var server = new LightweightRpcServer();
            server.AddEndPoint(new NamedPipeRpcEndPoint("Test"));

            using var publishedInstance = server.PublishInstance<ISimpleService>(new TestSimpleServiceImpl());
            server.Start();

            using var connection = new NamedPipeRpcConnection("Test");

            var s1 = connection.GetServiceInstance<ISimpleService>(publishedInstance.Value.ObjectId);
            var s2 = connection.GetServiceInstance<ISimpleService>(publishedInstance.Value.ObjectId);
            Assert.AreSame(s1, s2);
        }
    }
}
