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
            var connectionManager = Client.RpcExamplesHelper.CreateConnectionManager();

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
        }
    }
}
