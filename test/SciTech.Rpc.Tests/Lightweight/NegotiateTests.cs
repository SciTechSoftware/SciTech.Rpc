using NUnit.Framework;
using SciTech.Rpc.Client;
using SciTech.Rpc.Lightweight.Client;
using SciTech.Rpc.Lightweight.Server;
using SciTech.Rpc.Server;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Lightweight
{
    public class NegotiateTests
    {
        [Test]
        public async Task DefaultNegotiateTest()
        {
            using var server = new LightweightRpcServer();
            using var _ = server.PublishSingleton<INegotiateService>(() => new NegotiateServiceImpl());

            server.AddEndPoint(new TcpRpcEndPoint("127.0.0.1", 50052, false, new NegotiateServerOptions()));

            server.Start();
            try
            {

                var connection = new TcpRpcConnection(new RpcServerConnectionInfo(new Uri("lightweight.tcp://localhost:50052")), new NegotiateClientOptions
                {
                    Credential = new NetworkCredential("Andreas", "19+PqzY1a", "scitech" )
                });

                await connection.ConnectAsync(default).ContextFree();
                Assert.IsTrue(connection.IsConnected);
                Assert.IsTrue(connection.IsEncrypted);
                Assert.IsTrue(connection.IsSigned);

                var negotiateService = connection.GetServiceSingleton<INegotiateService>();
                string identity = negotiateService.GetIdentity();

                Assert.AreEqual(@"SCITECH\Andreas", identity);
            }
            finally
            {
                await server.ShutdownAsync().ContextFree();
            }
        }


        [RpcService]
        public interface INegotiateService
        {
            string GetIdentity();
        }

        public class NegotiateServiceImpl : INegotiateService
        {
            public string GetIdentity()
            {
                return "???";
            }
        }
    }
}
