using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SciTech.Rpc.CodeGen.Client;
using SciTech.Rpc.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SciTech.Rpc.CodeGen
{
    [Generator]
    public class RpcCodeGenerator : ISourceGenerator
    {
        private class GeneratedProxyAssembly
        {


        }



        public void Execute(GeneratorExecutionContext context)
        {
            if( context.SyntaxContextReceiver is RpcSyntaxReceiver rpcReceiver)
            {
                var builder = new RpcBuilderUtil(context.Compilation);

                Dictionary<string, int> definedProxyTypes = new Dictionary<string, int>();

                List<GeneratedProxyType> proxyTypes = new();

                foreach ( var typeSymbol in rpcReceiver.rpcInterfaces)
                {
                    var serviceInfo = RpcBuilderUtil.TryGetServiceInfoFromType(typeSymbol);

                    if( serviceInfo !=null )
                    { 
                        var allServices = RpcBuilderUtil.GetAllServices(typeSymbol, false);

                        var proxyBuilder = new RpcServiceProxyBuilder(allServices, context, definedProxyTypes);
                        var proxyCode = proxyBuilder.BuildProxy();
                        proxyTypes.Add(proxyCode);
                        // context.AddSource(serviceInfo.Name, proxyCode);
                    }

                    //var syntaxRef = typeSymbol.DeclaringSyntaxReferences.FirstOrDefault();
                    //var syntaxNode = syntaxRef?.GetSyntax();
                    //string name = typeSymbol.Name.Substring(1);

                    ////context.AddSource(name + ".cs", $"class {name} {{}}");

                    //var attributes = typeSymbol.GetAttributes();
                    //foreach( var attr in attributes )
                    //{
                    //    var attrClass = attr.AttributeClass;
                    //    var attrName = attrClass.Name;
                    //    var attrName2 = attrClass.ToString();
                    //    var assembly = attrClass.ContainingAssembly;
                    //    assembly = null;


                    //}
                    //var members = typeSymbol.GetMembers();
                    //var events = members.OfType<IEventSymbol>().Where(e => !e.IsStatic).ToList();
                    //var properties = members.OfType<IPropertySymbol>().Where(e => !e.IsStatic).ToList();

                    //Debug.WriteLine(typeSymbol.ToString());
                    ////var symbolInfo = model.GetSymbolInfo(rpcInterfaceSyntax);
                    ////if( symbolInfo.Symbol != null )
                    ////{
                    ////    Debug.WriteLine(symbolInfo.Symbol.ToString());
                    ////}

                    ////context.ReportDiagnostic(Diagnostic.Create(
                    ////    "RPC0001", "SciTech.Rpc", new LocalizableResourceString("", null, null),
                    ////    DiagnosticSeverity.Error, DiagnosticSeverity.Error, true, 1,
                    ////    location: syntaxNode?.GetLocation() ));
                }

                proxyTypes = null;

            }
            Debug.WriteLine("Execute code generator");
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            //Debugger.Launch();
            context.RegisterForSyntaxNotifications(()=>new RpcSyntaxReceiver());
            Debug.WriteLine("Initalize code generator");
        }

        class RpcSyntaxReceiver : ISyntaxContextReceiver
        {
            internal List<ITypeSymbol> rpcInterfaces = new();

            public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
            {
                SyntaxNode node = context.Node;
                //ISymbol symbol = context.SemanticModel.GetDeclaredSymbol(node);
                //if( symbol!= null)
                //{
                //    Debug.WriteLine($"{symbol.Kind}: {symbol.ToDisplayString()}");
                //}

                Debug.WriteLine(context.Node.GetType().ToString());

                if (node is InterfaceDeclarationSyntax interfaceSyntax && interfaceSyntax.AttributeLists.Count > 0 )
                {
                    ISymbol? symbol = context.SemanticModel.GetDeclaredSymbol(node);
                    if (symbol is ITypeSymbol typeSymbol && typeSymbol.TypeKind == TypeKind.Interface)
                    {
                        var typeAttributes = typeSymbol.GetAttributes();
                        var rpcAttribute = typeAttributes.FirstOrDefault(ad => ad.AttributeClass.ToString() == "SciTech.Rpc.RpcServiceAttribute");
                        if (rpcAttribute != null)
                        {
                            rpcInterfaces.Add(typeSymbol);
                        }
                    }

                    //foreach ( var attrList in interfaceSyntax.AttributeLists)
                    //{
                    //    foreach( var attr in attrList.Attributes)
                    //    {
                    //        if (attr.Name.ToString().Contains("RpcService"))
                    //        {
                    //            rpcInterfaces.Add(interfaceSyntax);
                    //            break;
                    //        }
                    //    }
                    //}
                    //var typeAttributes = typeSymbol.GetAttributes();
                    //var rpcAttribute = typeAttributes.FirstOrDefault(ad => ad.AttributeClass.ToString() == "SciTech.Rpc.RpcServiceAttribute");
                    //if( rpcAttribute != null )
                    //{

                    //}
                }
            }
        }
    }
}
