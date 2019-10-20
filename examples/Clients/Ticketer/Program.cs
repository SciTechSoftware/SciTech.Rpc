using SciTech.Rpc;
using SciTech.Rpc.Client;
using SciTech.Rpc.NetGrpc.Client;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using Ticketer;

namespace TicketerClient
{
    internal class Program
    {
        private const string Address = "https://localhost:50051";

        private static async Task Main(string[] args)
        {
            var sslOptions = TestCertificates.SslClientOptions;

            string? token = null;

            var options = new RpcClientOptions();
            options.Interceptors.Add(metadata=>
            {
                if (token != null)
                {
                    metadata.AddHeader("Authorization", $"Bearer {token}");
                }

                return Task.CompletedTask;
            });

            var connection = new NetGrpcServerConnection(Address, options);
            var client = connection.GetServiceSingleton<ITicketerServiceClient>();

            Console.WriteLine("gRPC Ticketer");
            Console.WriteLine();
            Console.WriteLine("Press a key:");
            Console.WriteLine("1: Get available tickets");
            Console.WriteLine("2: Purchase ticket (requires authorization)");
            Console.WriteLine("3: Track ticket purchases (requires authentication)");
            Console.WriteLine("4: Stop tracking ticket purchases (requires authentication)");
            Console.WriteLine("5: Authenticate");
            Console.WriteLine("6: Exit");
            Console.WriteLine();

            EventHandler<TicketPurchaseEventArgs> purchasedHandler = (s, e) =>
                   Console.WriteLine($"User '{e.User}' purchased '{e.TicketCount}' ticket(s).");

            var exiting = false;
            while (!exiting)
            {
                var consoleKeyInfo = Console.ReadKey(intercept: true);
                switch (consoleKeyInfo.KeyChar)
                {
                    case '1':
                        await GetAvailableTickets(client);
                        break;
                    case '2':
                        await PurchaseTicket(client, token);
                        break;
                    case '3':
                        try
                        {
                            Console.WriteLine("Starting ticket purchase tracking...");
                            client.TicketsPurchased += purchasedHandler;
                            await client.WaitForPendingEventHandlersAsync();

                            Console.WriteLine("Successfully started ticket purchase tracking.");
                        }
                        catch (Exception x)
                        {
                            Console.WriteLine("Error tracking ticket purchases." + Environment.NewLine + x.ToString());
                        }
                        break;
                    case '4':
                        try
                        {
                            Console.WriteLine("Stopping ticket purchase tracking...");
                            client.TicketsPurchased -= purchasedHandler;
                            await client.WaitForPendingEventHandlersAsync();

                            Console.WriteLine("Successfully stopped ticket purchase tracking.");
                        }
                        catch (Exception x)
                        {
                            Console.WriteLine("Error stopping ticket purchases tracking." + Environment.NewLine + x.ToString());
                        }
                        break;
                    case '5':
                        token = await Authenticate();
                        break;
                    case '6':
                        exiting = true;
                        break;
                }
            }

            Console.WriteLine("Exiting");
        }

        private static async Task<string> Authenticate()
        {
            Console.WriteLine($"Authenticating as {Environment.UserName}...");
            var httpClient = new HttpClient();
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri($"{Address}/generateJwtToken?name={HttpUtility.UrlEncode(Environment.UserName)}"),
                Method = HttpMethod.Get,
                Version = new Version(2, 0)
            };
            var tokenResponse = await httpClient.SendAsync(request);
            tokenResponse.EnsureSuccessStatusCode();

            var token = await tokenResponse.Content.ReadAsStringAsync();
            Console.WriteLine("Successfully authenticated.");

            return token;
        }

        private static async Task PurchaseTicket(ITicketerServiceClient client, string? token)
        {
            Console.WriteLine("Purchasing ticket...");
            try
            {
                var success = await client.BuyTicketsAsync(1);
                if (success)
                {
                    Console.WriteLine("Purchase successful.");
                }
                else
                {
                    Console.WriteLine("Purchase failed. No tickets available.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error purchasing ticket." + Environment.NewLine + ex.ToString());
            }
        }

        private static async Task GetAvailableTickets(ITicketerServiceClient client)
        {
            Console.WriteLine("Getting available ticket count...");
            var count = await client.GetAvailableTicketsAsync();
            Console.WriteLine("Available ticket count: " + count);
        }

    }
}
