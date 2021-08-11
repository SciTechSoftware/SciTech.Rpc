using Greeter;
using SciTech.Rpc;
using SciTech.Rpc.Client;
using SciTech.Rpc.Lightweight.Client;
using SciTech.Rpc.Lightweight.Server;
using SciTech.Rpc.Server;
using System;
using System.Threading.Tasks;

namespace GrpcGreeter
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            var credentials = TestCertificates.GrpcSslCredentials;
            var sslOptions = TestCertificates.SslClientOptions;

            var connectionManager = new RpcConnectionManager(
                new IRpcConnectionProvider[] {
                    new SciTech.Rpc.Grpc.Client.GrpcConnectionProvider(credentials),
                    new SciTech.Rpc.Lightweight.Client.LightweightConnectionProvider(sslOptions)
                });

            var connection = new NamedPipeRpcConnection("Test");
            connection.GetServiceSingleton<IGreeterServiceClient>();

            RpcConnectionInfo connectionInfo = Client.RpcExamplesHelper.RetrieveConnectionInfo();

            var greeter = connectionManager.GetServiceSingleton<IGreeterServiceClient>(connectionInfo);

            var reply = greeter.SayHello("GreeterClient");

            Console.WriteLine();
            Console.WriteLine(reply);

            Console.WriteLine();
            Console.WriteLine("Shutting down");

            await connectionManager.ShutdownAsync().ConfigureAwait(false);

            Console.WriteLine("Press any key to exit...");

            Console.ReadKey();


            //var server = new LightweightRpcServer();
            //server.AddEndPoint(new NamedPipeRpcEndPoint("GreeterPipe"));
            //server.PublishSingleton<IGreeterService>(new GreeterServiceImpl());
            //server.Start();


            //Console.WriteLine("Press any key to exit...");

            //Console.ReadKey();


            //var connection = new NamedPipeRpcConnection("GreeterPipe");
            //var greeter2 = connection.GetServiceSingleton<IGreeterServiceClient>();

            


        }
    }
}
