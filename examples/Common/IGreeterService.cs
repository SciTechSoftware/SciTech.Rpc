using SciTech.Rpc;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Greeter
{
    [RpcService]
    public interface IGreeterService
    {
        string SayHello(string name);
    }

    [RpcService(ServerDefinitionType = typeof(IGreeterService))]
    public interface IGreeterServiceClient : IGreeterService
    {
        Task<string> SayHelloAsync(string name);
    }
}
