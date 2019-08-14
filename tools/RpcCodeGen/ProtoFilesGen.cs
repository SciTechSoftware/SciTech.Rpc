#define COREFX
using ProtoBuf;
using ProtoBuf.Meta;
using SciTech.Rpc;
using SciTech.Rpc.Internal;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace RpcCodeGen
{
    public class ProtoFilesGen
    {
        private readonly string[] assemblies;

        public ProtoFilesGen(ProtoOptions options)
        {
            this.assemblies = options.Assemblies.ToArray();
        }

        public int GenerateFiles()
        {
            foreach (var assemblyPath in this.assemblies)
            {
                Assembly assembly;
                try
                {
                    assembly = Assembly.LoadFrom(assemblyPath);
                }
                catch (Exception e)
                {
                    LogError(1, $"Failed to load assembly '{assemblyPath}'.", e);
                    throw;
                }

                var directory = Path.GetDirectoryName(assemblyPath);
                var name = Path.GetFileNameWithoutExtension(assemblyPath);

                var protoPath = Path.Combine(directory, $"{name}.proto");
                using (var writer = new StreamWriter(protoPath, false, Encoding.UTF8))
                {
                    this.WriteProto(assembly, writer);
                }
            }

            return 0;
        }

        private static void AddMemberAttribute(FieldBuilder fieldBuilder, int index)
        {
            var memmberCtor = typeof(ProtoMemberAttribute).GetConstructor(new Type[] { typeof(int) });
            var memberAttribBuilder = new CustomAttributeBuilder(memmberCtor, new object[] { index });
            fieldBuilder.SetCustomAttribute(memberAttribBuilder);
        }

        private static ModuleBuilder CreateModuleBuilder(string assemblyName, string moduleName, bool save)
        {

#if COREFX
            AssemblyName an = new AssemblyName { Name = assemblyName };
            AssemblyBuilder asm = AssemblyBuilder.DefineDynamicAssembly(an,
                AssemblyBuilderAccess.Run);
            ModuleBuilder module = asm.DefineDynamicModule(moduleName);
#else
            AssemblyName an = new AssemblyName { Name = assemblyName };
            AssemblyBuilder asm = AppDomain.CurrentDomain.DefineDynamicAssembly(an,
                save ? AssemblyBuilderAccess.RunAndSave : AssemblyBuilderAccess.Run);
            ModuleBuilder module = save ? asm.DefineDynamicModule(moduleName, path)
                                        : asm.DefineDynamicModule(moduleName);
#endif
            return module;
        }

        private static void LogError(int errNo, string str, Exception e = null)
        {
            // TODO: Output in a format suitable for an MsBuild task
            Console.WriteLine($"ERROR {errNo}: {str}");
            if (e != null)
            {
                Console.WriteLine(e.Message);
            }
        }

        private static void LogWarning(int warningNo, string str, Exception e = null)
        {
            // TODO: Output in a format suitable for an MsBuild task
            Console.WriteLine($"WARNING {warningNo}: {str}");
            if (e != null)
            {
                Console.WriteLine(e.Message);
            }
        }

        private Type CreateRequestType(ModuleBuilder dynamicModuleBuilder, RpcOperationInfo opInfo)
        {
            try
            {
                var typeBuilder = dynamicModuleBuilder.DefineType($"{opInfo.Service.Name}_{opInfo.Name}Request");
                var contractCtor = typeof(ProtoContractAttribute).GetConstructor(Type.EmptyTypes);
                var attribBuilder = new CustomAttributeBuilder(contractCtor, Array.Empty<object>());
                typeBuilder.SetCustomAttribute(attribBuilder);

                var parameters = opInfo.Method.GetParameters();
                int paramIndex = 0;
                foreach (var argType in opInfo.RequestParameters)
                {
                    FieldBuilder fieldBuilder;
                    if (paramIndex == 0)
                    {
                        fieldBuilder = typeBuilder.DefineField("Id", typeof(RpcObjectId), FieldAttributes.Public);
                    }
                    else
                    {
                        fieldBuilder = typeBuilder.DefineField(parameters[paramIndex - 1].Name, argType.Type, FieldAttributes.Public);
                    }

                    AddMemberAttribute(fieldBuilder, paramIndex + 1);

                    paramIndex++;
                }

                return typeBuilder.CreateType();
            }
            catch (Exception e)
            {
                LogWarning(2, $"Failed to create request type for operation '{opInfo.FullName}'", e);
            }

            return null;
        }

        private Type CreateResponseType(ModuleBuilder dynamicModuleBuilder, RpcOperationInfo opInfo)
        {
            try
            {
                var typeBuilder = dynamicModuleBuilder.DefineType($"{opInfo.Service.Name}_{opInfo.Name}Reply");
                var contractCtor = typeof(ProtoContractAttribute).GetConstructor(Type.EmptyTypes);
                var attribBuilder = new CustomAttributeBuilder(contractCtor, Array.Empty<object>());
                typeBuilder.SetCustomAttribute(attribBuilder);

                var errorField = typeBuilder.DefineField("Error", typeof(RpcError), FieldAttributes.Public);
                AddMemberAttribute(errorField, 1);
                if (!Equals(opInfo.ResponseReturnType, typeof(void)))
                {
                    var resultField = typeBuilder.DefineField("Result", opInfo.ResponseReturnType, FieldAttributes.Public);
                    AddMemberAttribute(resultField, 2);
                }
                _ = opInfo.Method.GetParameters();
                return typeBuilder.CreateType();
            }
            catch (Exception e)
            {
                LogWarning(2, $"Failed to create reply type for operation '{opInfo.FullName}'", e);
            }

            return null;
        }

        private void WriteProto(Assembly assembly, TextWriter output)
        {
            var dynamicModuleBuilder = CreateModuleBuilder("RequestResponseTypes", "RequestResponseTypes.dll", false);

            StringBuilder serviceBuilder = new StringBuilder();

            RuntimeTypeModel typeModel = TypeModel.Create();
            foreach (var exportedType in assembly.GetExportedTypes())
            {
                var serviceInfo = RpcBuilderUtil.TryGetServiceInfoFromType(exportedType);
                if (serviceInfo != null && serviceInfo.DefinitionSide != RpcServiceDefinitionSide.Client)
                {
                    serviceBuilder.AppendLine();
                    serviceBuilder.AppendLine($"service {serviceInfo.Name} {{");

                    foreach (var rpcMemberInfo in RpcBuilderUtil.EnumOperationHandlers(serviceInfo, true))
                    {
                        if (rpcMemberInfo is RpcOperationInfo rpcOpInfo)
                        {
                            var namedRequestType = this.CreateRequestType(dynamicModuleBuilder, rpcOpInfo);
                            var namedResponseType = this.CreateResponseType(dynamicModuleBuilder, rpcOpInfo);
                            if (namedRequestType == null || namedResponseType == null)
                            {
                                continue;   // Should probably stop generator.
                            }

                            typeModel.Add(namedRequestType, true);
                            typeModel.Add(namedResponseType, true);

                            serviceBuilder.AppendLine(
                                $"\trpc {rpcOpInfo.Name} ({namedRequestType.Name}) returns ({namedResponseType.Name});");

                        }
                    }

                    serviceBuilder.AppendLine("}");
                }
            }

            string schema = typeModel.GetSchema(null, ProtoSyntax.Proto3);
            output.WriteLine(schema);
            output.WriteLine(serviceBuilder.ToString());
        }
    }
}
