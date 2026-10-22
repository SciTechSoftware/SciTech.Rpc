using SciTech.Rpc;
using SciTech.Rpc.Client;
using SciTech.Rpc.Lightweight.Client;
using SciTech.Rpc.NetGrpc.Client;
using System;
using System.Net.Http;

namespace Client
{
    internal static class RpcExamplesHelper
    {
        /// <summary>
        /// Creates a connection manager that can connect to both the NetGrpcServer example (gRPC)
        /// and the LightweightServer example (lightweight RPC).
        /// </summary>
        public static RpcConnectionManager CreateConnectionManager()
        {
            var channelOptions = new Grpc.Net.Client.GrpcChannelOptions
            {
                HttpHandler = new HttpClientHandler
                {
                    // Just for testing! Allow any server certificate.
                    ServerCertificateCustomValidationCallback = (request, certificate, chain, sslPolicyErrors) => true
                }
            };

            return new RpcConnectionManager(
                new IRpcConnectionProvider[] {
                    new NetGrpcConnectionProvider(channelOptions: channelOptions),
                    new LightweightConnectionProvider(TestCertificates.SslClientOptions)
                });
        }

        public static RpcConnectionInfo RetrieveConnectionInfo()
        {
            while (true)
            {
                Console.WriteLine("--- Select how to connect to the RPC server:");
                Console.WriteLine("1. Use gRPC (start the NetGrpcServer example)");
                Console.WriteLine("2. Use lightweight RPC (start the LightweightServer example)");
                Console.Write("Make your choice (1 or 2): ");

                string choiceText = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(choiceText))
                {
                    return null;
                }

                if (int.TryParse(choiceText, out var choice))
                {
                    switch (choice)
                    {
                        case 1:
                            return new RpcConnectionInfo(new Uri("grpc://localhost:50051"));
                        case 2:
                            return new RpcConnectionInfo(new Uri("lightweight.tcp://localhost:50052"));
                    }
                }

                Console.WriteLine("Invalid choice, try again.");
                Console.WriteLine();
            }
        }
    }
}
