using NUnit.Framework;
//using Pluralsight.Crypto;
using SciTech.Rpc.Client;
using SciTech.Rpc.Grpc.Client;
using SciTech.Rpc.Grpc.Server;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server;
using SciTech.Rpc.Tests;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests.Grpc
{
    [TestFixture]
    public class GrpcSecurityTests : GrpcCoreFullStackTestsBase
    {
        //private static X509Certificate2 selfSignedCertificate;
        private IRpcSerializer serializer = new ProtobufRpcSerializer();

        //[Test]
        //public async Task SelfSignedServerTest()
        //{
        //    var serverBuilder = new RpcServiceDefinitionBuilder();
        //    serverBuilder
        //        .RegisterService<IBlockingService>()
        //        .RegisterService<ISimpleService>();
        //    var host = new GrpcServerHost(serverBuilder, this.serializer);

        //    host.AddEndPoint(new GrpcHostEndPoint("localhost", GrpcTestPort, true, true));

        //    host.Start();

        //    try
        //    {
        //        var serviceImpl = new TestBlockingSimpleServiceImpl();
        //        using (var publishScope = host.PublishServiceInstance(serviceImpl))
        //        {
        //            var objectId = publishScope.Value.ObjectId;

        //            var proxyGenerator = new GrpcProxyProvider();
        //            var connection = new GrpcConnection(new TcpRpcServerConnectionInfo("localhost", GrpcTestPort), proxyGenerator, this.serializer);

        //            var clientService = connection.GetRpcServiceInstance<IBlockingServiceClient>(objectId);

        //            int blockingRes = clientService.Add(12, 13);
        //            Assert.AreEqual(12 + 13, blockingRes);

        //        }
        //    }
        //    catch (Exception e)
        //    {
        //        Console.WriteLine(e.Message);
        //        throw;
        //    }
        //    finally
        //    {
        //        await host.ShutdownAsync();
        //    }
        //}
        //internal static X509Certificate2 GetSelfSignedCertificate()
        //{
        //    if (selfSignedCertificate == null)
        //    {
        //        using (var cryptContext = new Pluralsight.Crypto.CryptContext())
        //        {
        //            cryptContext.Open();
        //            Pluralsight.Crypto.SelfSignedCertProperties props = new Pluralsight.Crypto.SelfSignedCertProperties
        //            {
        //                IsPrivateKeyExportable = false,
        //                KeyBitLength = 2048,
        //                Name = new X500DistinguishedName("CN=nmpcore"),
        //                ValidFrom = DateTime.UtcNow - TimeSpan.FromDays(1),
        //                ValidTo = DateTime.UtcNow + TimeSpan.FromDays(365)
        //            };
        //            selfSignedCertificate = cryptContext.CreateSelfSignedCertificate(props);
        //        }
        //    }
        //    return selfSignedCertificate;
        //}
    }
}
