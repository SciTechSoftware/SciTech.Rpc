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
    }

    [RpcService]
    public interface IGreeterService : IBaseService
    {
        string GreeterName { get; }
        string SayHello(string name);
    }

    [RpcService(ServerDefinitionType = typeof(IGreeterService))]
    public interface IGreeterServiceClient : IGreeterService
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
            var location = typeof(RpcServiceAttribute).GetTypeInfo().Assembly.Location;
            return CSharpCompilation.Create("compilation",
                  new[] { CSharpSyntaxTree.ParseText(source) },
                  new[] { 
                      MetadataReference.CreateFromFile(typeof(Binder).GetTypeInfo().Assembly.Location),
                      MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).GetTypeInfo().Assembly.Location)},
                  new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }
    }
}
