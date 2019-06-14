using SciTech.Rpc;
using System;

namespace Client
{
    internal static class RpcExamplesHelper
    {
        public static RpcServerConnectionInfo RetrieveConnectionInfo()
        {
            while (true)
            {
                Console.WriteLine("--- Select how to connect to the RPC server:");
                Console.WriteLine("1. Use gRPC");
                Console.WriteLine("2. Use lightweight RPC");
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
                            return new RpcServerConnectionInfo(new Uri("grpc://localhost:50051"));
                        case 2:
                            return new RpcServerConnectionInfo(new Uri("lightweight.tcp://localhost:50052"));
                    }
                }

                Console.WriteLine("Invalid choice, try again.");
                Console.WriteLine();
            }
        }
    }
}
