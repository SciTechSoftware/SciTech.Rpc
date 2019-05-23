#define COREFX
using CommandLine;
using ProtoBuf;
using ProtoBuf.Meta;
using SciTech.Rpc;
using SciTech.Rpc.Internal;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace RpcCodeGen
{

    [Verb("proto", HelpText = "Generates .proto files from RPC services in the provided assemblies.")]
    public class ProtoOptions
    {
        [Value(0)]
        public IEnumerable<string> Assemblies { get; set; }

    }

    [Verb("aot", HelpText = "Generates pre-compiled RPC assemblies sutiable for AOT compilation.")]
    public class AotOptions
    {
        [Value(0)]
        public IEnumerable<string> Assemblies { get; set; }

    }

    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                CommandLine.Parser.Default.ParseArguments<ProtoOptions, AotOptions>(args)
                    .WithParsed((ProtoOptions protoOptions) => GenerateProtoFiles(protoOptions))
                    .WithParsed((AotOptions aotOptions) => GenerateAotFiles(aotOptions));

                return 0;
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch ( Exception )
            {
                return 1;
            }
#pragma warning restore CA1031 // Do not catch general exception types

        }
        private static int GenerateProtoFiles(ProtoOptions protoOptions)
        {
            var filesGen = new ProtoFilesGen(protoOptions);

            return filesGen.GenerateFiles();
        }

        private static int GenerateAotFiles(AotOptions protoOptions)
        {
            foreach (var assembly in protoOptions.Assemblies)
            {
                Console.WriteLine(assembly);
            }

            return 0;
        }



    }
}
