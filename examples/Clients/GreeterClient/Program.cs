using Greeter;
using SciTech.Rpc;
using SciTech.Rpc.Client;
using System;
using System.Threading.Tasks;

namespace GrpcGreeter
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            var credentials = TestCertificates.SslCredentials;
            var connectionManager = new RpcServerConnectionManager(
                new IRpcConnectionProvider[] {
                    new SciTech.Rpc.Grpc.Client.GrpcConnectionProvider(credentials),
                    new SciTech.Rpc.Pipelines.Client.PipelinesConnectionProvider()
                });
            
            var connectionInfo = Client.RpcExamplesHelper.RetrieveConnectionInfo();

            var greeter = connectionManager.GetServiceSingleton<IGreeterServiceClient>(connectionInfo);

            var reply = greeter.SayHello("GreeterClient");

            Console.WriteLine();
            Console.WriteLine(reply);

            Console.WriteLine();
            Console.WriteLine("Shutting down");

            await connectionManager.ShutdownAsync().ConfigureAwait(false);

            Console.WriteLine("Press any key to exit...");

            Console.ReadKey();


        }
    }
}
