using Microsoft.Extensions.Logging;
using NUnit.Framework;
using SciTech.Rpc.Lightweight.Server;
using SciTech.Rpc.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Lightweight
{
    [TestFixture]
    public class LightweightServiceDiscoveryTests
    {
        private ILoggerFactory loggerFactory;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            this.loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            this.loggerFactory?.Dispose();
            this.loggerFactory = null;
        }


        [Test]
        public async Task Test()
        { 
            var server = new LightweightRpcServer();
            var connectionInfo = new RpcServerConnectionInfo(new Uri("test://test"));
            server.AddEndPoint(new LightweightDiscoveryEndPoint(connectionInfo));
            server.Start();
            try
            {

            }
            finally
            {
                await server.ShutdownAsync();
            }

            // server.PublishSingleton<ISimpleService>();
        }
    }
}
