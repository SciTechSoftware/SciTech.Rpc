using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SciTech.Rpc.CodeGen
{
    static class Program
    {
        public static void Main()
        {
            Compilation inputCompilation = CreateCompilation(@"
using SciTech.Rpc;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Greeter
{
    [RpcService]
    public interface IBaseService
    {
        string Name { get; }

        void Update(int value);
    }

    [RpcService(ServerDefinitionType = typeof(IBaseService))]
    public interface IBaseServiceClient
    {
        Task UpdateAsync(int value);
    }

    [RpcService]
    public interface IGreeterService : IBaseService
    {
        string GreeterName { get; }
        string SayHello(string name);
    }

    [RpcService(ServerDefinitionType = typeof(IGreeterService))]
    public interface IGreeterServiceClient : IGreeterService, IBaseServiceClient
    {
        Task<string> SayHelloAsync(string name);
    }
}
");
            var generator = new RpcCodeGenerator();

            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
            driver = driver.RunGeneratorsAndUpdateCompilation(inputCompilation, out var outputCompilation, out var diagnostics);

            foreach (var syntaxTree in outputCompilation.SyntaxTrees)
            {
                Console.WriteLine($"{syntaxTree.FilePath}:\r\n{syntaxTree.ToString()}");
            }
        }

        private static Compilation CreateCompilation(string source)
        {
            var location = @"C:\Sci\Libraries\SciTech.Rpc\src\SciTech.Rpc\bin\Debug\net5.0\";// typeof(RpcServiceAttribute).GetTypeInfo().Assembly.Location;
            return CSharpCompilation.Create("compilation",
                  new[] { CSharpSyntaxTree.ParseText(source) },
                  new[] { 
                      MetadataReference.CreateFromFile(typeof(Binder).GetTypeInfo().Assembly.Location),
                      MetadataReference.CreateFromFile(location+"SciTech.Rpc.dll"),
                      MetadataReference.CreateFromFile(location+"SciTech.Core.dll")
                  },
                  new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }
    }
}
