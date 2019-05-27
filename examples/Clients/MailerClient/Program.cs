using Mailer;
using SciTech.Rpc;
using SciTech.Rpc.Client;
using System;
using System.Threading.Tasks;

namespace MailerClient
{
    internal class Program
    {
        private static string GetMailboxName(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("No mailbox name provided. Using default name. Usage: dotnet run <name>.");
                return "DefaultMailbox";
            }

            return args[0];
        }

        private static async Task Main(string[] args)
        {
            var mailboxName = GetMailboxName(args);

            var credentials = TestCertificates.SslCredentials;
            var connectionManager = new RpcServerConnectionManager(
                new IRpcConnectionProvider[] {
                    new SciTech.Rpc.Grpc.Client.GrpcConnectionProvider(credentials),
                    new SciTech.Rpc.Pipelines.Client.PipelinesConnectionProvider()
                });

            var connectionInfo = Client.RpcExamplesHelper.RetrieveConnectionInfo();

            Console.WriteLine($"Connecting to mailbox '{mailboxName}'");
            Console.WriteLine();

            var mailBoxManager = connectionManager.GetServiceSingleton<IMailBoxManagerServiceClient>(connectionInfo);
            //await mailBoxManager.ConnectAsync();

            Console.WriteLine("Connected");
            Console.WriteLine("Press escape to disconnect. Press any other key to forward mail.");

            using (var mailbox = await mailBoxManager.GetMailboxAsync(mailboxName))
            {
                mailbox.MailReceieved += (s, msg) =>
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine();
                    Console.WriteLine("Mail received");
                    Console.WriteLine($"New mail: {msg.New}, Forwarded mail: {msg.Forwarded}");
                    Console.ResetColor();
                };

                mailbox.MailForwarded += (s, msg) =>
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine();
                    Console.WriteLine("Mail forwarded");
                    Console.WriteLine($"New mail: {msg.New}, Forwarded mail: {msg.Forwarded}");
                    Console.ResetColor();
                };

                while (true)
                {
                    var result = Console.ReadKey(intercept: true);
                    if (result.Key == ConsoleKey.Escape)
                    {
                        break;
                    }

                    await mailbox.ForwardMailAsync();
                }
            }

            Console.WriteLine("Disconnecting");
            await connectionManager.ShutdownAsync();

            Console.WriteLine("Disconnected. Press any key to exit.");
            Console.ReadKey();
        }
    }
}
