using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace SciTech.Rpc.Analyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class RpcAnalyzer : DiagnosticAnalyzer
    {
        public const string RpcDuplicateOperationRuleId = "RPC1001";

        public const string RpcMissingServerOperationRuleId = "RPC1002";

        public const string RpcOperationMbrArgumentId = "RPC1003";

        public const string RpcOperationMbrReturnId = "RPC1004";

        public const string RpcOperationRefArgumentId = "RPC1005";

        public const string RpcOperationServiceArgumentId = "RPC1006";


        private const string Category = "RPC";

        public static DiagnosticDescriptor RpcDuplicateOperationRule = new DiagnosticDescriptor(
            RpcDuplicateOperationRuleId,
            "Operation has already been defined.",
            "Operation '{0}' has been defined more than once in server side RPC definition.",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Server side RPC operation must have a unique name.");

        private static DiagnosticDescriptor RpcMissingServerOperationRule = new DiagnosticDescriptor(
            RpcMissingServerOperationRuleId,
            "Operation does not exist in server side RPC definition.",
            "Operation '{0}' does not exist in server side RPC definition.",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Client side RPC operation must have a matching server side operation.");

        private static DiagnosticDescriptor RpcOperationMbrArgumentRule = new DiagnosticDescriptor(
            RpcOperationMbrArgumentId,
            "MarshalByRefObject cannot be used as parameter in RPC operation.",
            "MarshalByRefObject '{0}' cannot be used as parameter in RPC operation.",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "MarshalByRefObjects cannot be used as parameter in RPC operation.");

        private static DiagnosticDescriptor RpcOperationMbrReturnRule = new DiagnosticDescriptor(
            RpcOperationMbrReturnId,
            "MarshalByRefObject cannot be returned from an RPC operation.",
            "MarshalByRefObject '{0}' cannot be return from RPC operation.",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "MarshalByRefObjects cannot be returned from RPC operation.");

        private static DiagnosticDescriptor RpcOperationRefArgumentRule = new DiagnosticDescriptor(
            RpcOperationRefArgumentId,
            "An RPC operation parameter cannot be passed as reference (ref/in/out).",
            "RPC operation parameter '{0}' cannot be passed as reference.",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "An RPC operation parameter cannot be passed as reference (ref/in/out).");

        private static DiagnosticDescriptor RpcOperationServiceArgumentRule = new DiagnosticDescriptor(
            RpcOperationServiceArgumentId,
            "RPC service cannot be used as parameter in RPC operation.",
            "Rpc service '{0}' cannot be used as parameter in RPC operation.",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "RPC services cannot be used as parameter in RPC operation.");
        private enum RpcServiceDefinitionSide
        {
            Both,
            Client,
            Server
        }

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get
            {
                return ImmutableArray.Create(
                    RpcOperationMbrArgumentRule,
                    RpcOperationServiceArgumentRule,
                    RpcMissingServerOperationRule,
                    RpcOperationRefArgumentRule,
                    RpcDuplicateOperationRule, 
                    RpcOperationMbrReturnRule);
            }
        }

        public static IEnumerable<RpcOperation> EnumRpcOperations(ITypeSymbol rpcTypeSymbol)
        {
            if (rpcTypeSymbol is ITypeSymbol serverTypeSymbol)
            {
                var serverTypeMembers = serverTypeSymbol.GetMembers();

                // TODO: Operation type, request type and response type should also be retrieved
                HashSet<ISymbol> handledMembers = new HashSet<ISymbol>();

                foreach (var member in serverTypeMembers)
                {
                    if (member is IPropertySymbol propertySymbol)
                    {
                        if (propertySymbol.GetMethod != null && propertySymbol.GetMethod.DeclaredAccessibility == Accessibility.Public)
                        {
                            string operationName = $"Get{propertySymbol.Name}";
                            yield return new RpcOperation(member, operationName);

                            handledMembers.Add(propertySymbol.GetMethod);
                        }

                        if (propertySymbol.SetMethod != null && propertySymbol.SetMethod.DeclaredAccessibility == Accessibility.Public)
                        {
                            string operationName = $"Set{propertySymbol.Name}";
                            yield return new RpcOperation(member, operationName);

                            handledMembers.Add(propertySymbol.SetMethod);
                        }
                    }

                    if (member is IEventSymbol eventSymbol)
                    {
                        if (eventSymbol.AddMethod != null)
                        {
                            // TODO: Lookup  server side operation
                            handledMembers.Add(eventSymbol.AddMethod);
                        }

                        if (eventSymbol.RemoveMethod != null)
                        {
                            // TODO: Lookup  server side operation
                            handledMembers.Add(eventSymbol.RemoveMethod);
                        }
                    }
                }

                foreach (var member in serverTypeMembers)
                {
                    if (!(member is IPropertySymbol) && !(member is IEventSymbol)
                        && !handledMembers.Contains(member))
                    {
                        string operationName = GetOperationNameFromMemberSymbol(member);
                        yield return new RpcOperation(member, operationName);
                    }
                }
            }
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
            context.EnableConcurrentExecution();

            context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
            context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
            context.RegisterSymbolAction(AnalyzeFieldAndProperty, SymbolKind.Field, SymbolKind.Property);
        }

        internal static AttributeData? GetRpcServiceAttribute(ISymbol symbol)
        {
            if (symbol is ITypeSymbol typeSymbol && typeSymbol.TypeKind == TypeKind.Interface)
            {
                var typeAttributes = typeSymbol.GetAttributes();
                return typeAttributes.FirstOrDefault(ad => ad.AttributeClass.ToString() == "SciTech.Rpc.RpcServiceAttribute");
            }

            return null;
        }

        internal static bool IsMarshalByRef(INamedTypeSymbol type)
        {
            string? typeName = type?.ToString();
            if (typeName == "System.MarshalByRefObject")
            {
                return true;
            }

            return false;
        }


        internal static bool IsMarshalByRefType(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol namedTypeSymbol)
            {
                if (IsMarshalByRef(namedTypeSymbol))
                {
                    return true;
                }

                if (namedTypeSymbol.BaseType == null)
                {
                    return false;
                }

                return IsMarshalByRefType(namedTypeSymbol.BaseType);
            }

            if (type is IArrayTypeSymbol arrayTypeSymbol)
            {
                return IsMarshalByRefType(arrayTypeSymbol.ElementType);
            }

            return false;
        }


        internal static bool IsRpcServiceInterface(ISymbol symbol)
        {
            return GetRpcServiceAttribute(symbol) != null;
        }

        internal static bool IsRpcServiceType(ITypeSymbol type, bool declaredInterfacesOnly = false, bool allowArrays = false)
        {
            if (IsRpcServiceInterface(type))
            {
                return true;
            }

            if (allowArrays && type is IArrayTypeSymbol arrayTypeSymbol)
            {
                return IsRpcServiceType(arrayTypeSymbol.ElementType);
            }

            var allInterfaces = declaredInterfacesOnly ? type?.Interfaces : type?.AllInterfaces;
            if (allInterfaces != null)
            {
                foreach (var interfaceSymbol in allInterfaces.Value)
                {
                    if (IsRpcServiceInterface(interfaceSymbol))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void AnalyzeFieldAndProperty(SymbolAnalysisContext context)
        {
            try
            {
                if (context.Symbol is IPropertySymbol propertySymbol)
                {
                    if (propertySymbol.ContainingType != null)
                    {
                        var typeAttributes = propertySymbol.ContainingType.GetAttributes();
                        var rpcServiceAttribute = typeAttributes.FirstOrDefault(ad => ad.AttributeClass.ToString() == "SciTech.Rpc.RpcServiceAttribute");

                        if (rpcServiceAttribute != null)
                        {
                            if (propertySymbol.GetMethod != null)
                            {
                                AnalyzeServerSideOperation(context, propertySymbol.GetMethod, $"Get{propertySymbol.Name}", rpcServiceAttribute);
                            }
                            if (propertySymbol.GetMethod != null)
                            {
                                AnalyzeServerSideOperation(context, propertySymbol.SetMethod, $"Set{propertySymbol.Name}", rpcServiceAttribute);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                HandleAnalysisException(e);
            }
        }


        private static void AnalyzeMethod(SymbolAnalysisContext context)
        {
            try
            {
                var methodSymbol = (IMethodSymbol)context.Symbol;

                if (methodSymbol.ContainingType != null)
                {
                    var rpcServiceAttribute = GetRpcServiceAttribute(methodSymbol.ContainingType);

                    if (rpcServiceAttribute != null)
                    {
                        AnalyzeServerSideMethod(context, methodSymbol, rpcServiceAttribute);

                        if( IsMarshalByRefType( methodSymbol.ReturnType))
                        {
                            var mbrDiagnostic = Diagnostic.Create(RpcOperationMbrReturnRule, methodSymbol.Locations[0], methodSymbol.ReturnType.Name);
                            context.ReportDiagnostic(mbrDiagnostic);
                        }

                        foreach (var arg in methodSymbol.Parameters)
                        {
                            if (IsMarshalByRefType(arg.Type))
                            {
                                var mbrDiagnostic = Diagnostic.Create(RpcOperationMbrArgumentRule, arg.Locations[0], arg.Type.Name);
                                context.ReportDiagnostic(mbrDiagnostic);
                            }
                            else if (IsRpcServiceType(arg.Type, allowArrays: true))
                            {
                                var mbrDiagnostic = Diagnostic.Create(RpcOperationServiceArgumentRule, arg.Locations[0], arg.Type.Name);
                                context.ReportDiagnostic(mbrDiagnostic);
                            }
                            if (arg.RefKind != RefKind.None)
                            {
                                var mbrDiagnostic = Diagnostic.Create(RpcOperationRefArgumentRule, arg.Locations[0], arg.Name);
                                context.ReportDiagnostic(mbrDiagnostic);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (!HandleAnalysisException(e)) throw;
            }
        }


        private static void AnalyzeServerSideMethod(SymbolAnalysisContext context, IMethodSymbol methodSymbol, AttributeData rpcServiceAttribute)
        {
            if (methodSymbol.AssociatedSymbol != null)
            {
                return;
            }

            string clientOperationName = GetOperationNameFromMemberSymbol(methodSymbol);
            AnalyzeServerSideOperation(context, methodSymbol, clientOperationName, rpcServiceAttribute);
        }

        private static void AnalyzeServerSideOperation(SymbolAnalysisContext context, ISymbol methodSymbol, string clientOperationName, AttributeData rpcServiceAttribute)
        {
            var serverDefinitionType = rpcServiceAttribute.NamedArguments.FirstOrDefault(pair => pair.Key == "ServerDefinitionType").Value;

            if (!serverDefinitionType.IsNull)
            {
                if (!string.IsNullOrEmpty(clientOperationName))
                {
                    if (serverDefinitionType.Value is ITypeSymbol serverTypeSymbol)
                    {
                        bool hasServerOperation = false;

                        foreach (var serverOp in EnumRpcOperations(serverTypeSymbol))
                        {
                            if (clientOperationName == serverOp.OperationName)
                            {
                                hasServerOperation = true;
                                break;
                            }
                        }

                        if (!hasServerOperation)
                        {
                            var mbrDiagnostic = Diagnostic.Create(RpcMissingServerOperationRule, methodSymbol.Locations[0], clientOperationName);
                            context.ReportDiagnostic(mbrDiagnostic);
                        }
                    }

                }
            }
        }

        private static void AnalyzeType(SymbolAnalysisContext context)
        {
            try
            {
                var serviceAttribute = GetRpcServiceAttribute(context.Symbol);

                if (serviceAttribute != null)
                {
                    var definitionSide = GetDefinitionSide(serviceAttribute);

                    if (definitionSide != RpcServiceDefinitionSide.Client)
                    {
                        foreach (var operationGroup in EnumRpcOperations((ITypeSymbol)context.Symbol).GroupBy(op => op.OperationName))
                        {
                            if (operationGroup.Count() > 1)
                            {
                                foreach (var op in operationGroup)
                                {
                                    var mbrDiagnostic = Diagnostic.Create(RpcDuplicateOperationRule, op.Member.Locations[0], op.OperationName);
                                    context.ReportDiagnostic(mbrDiagnostic);
                                }
                            }

                        }
                    }
                }
            }
            catch (Exception e)
            {
                HandleAnalysisException(e);
            }

        }

        private static RpcServiceDefinitionSide GetDefinitionSide(AttributeData serviceAttribute)
        {
            var serverDefinitionType = serviceAttribute.NamedArguments.FirstOrDefault(pair => pair.Key == "ServerDefinitionType").Value;
            var serverDefinitionTypeName = serviceAttribute.NamedArguments.FirstOrDefault(pair => pair.Key == "ServerDefinitionTypeName").Value;
            var serviceDefinitionSide = serviceAttribute.NamedArguments.FirstOrDefault(pair => pair.Key == "ServiceDefinitionSide").Value;

            RpcServiceDefinitionSide definitionSide;

            if (!serviceDefinitionSide.IsNull && serviceDefinitionSide.Value is int iSide)
            {
                definitionSide = (RpcServiceDefinitionSide)iSide;
            }
            else
            {
                if (!serverDefinitionType.IsNull || !serverDefinitionTypeName.IsNull)
                {
                    definitionSide = RpcServiceDefinitionSide.Client;
                }
                else
                {
                    definitionSide = RpcServiceDefinitionSide.Both;
                }
            }

            return definitionSide;
        }

        private static string GetOperationNameFromMemberSymbol(ISymbol symbol)
        {
            if (symbol.DeclaredAccessibility == Accessibility.Public)
            {
                if (symbol is IMethodSymbol methodSymbol)
                {
                    string methodName = methodSymbol.Name;

                    string typeName = methodSymbol.ReturnType.ToString();
                    if (typeName.StartsWith("System.Threading.Tasks.Task") && methodName.EndsWith("Async"))
                    {
                        methodName = methodName.Substring(0, methodName.Length - "Async".Length);
                    }

                    return methodName;
                }
            }

            return "";
        }

        private static bool HandleAnalysisException(Exception e)
        {
            // File.AppendAllText(@"d:\scrap\RpcAnalyzerError.txt", $"{e.Message}\r\n{e.StackTrace}\r\n");
            return false;
        }

        public class RpcOperation
        {
            public RpcOperation(ISymbol member, string operationName)
            {
                this.Member = member;
                this.OperationName = operationName;
            }

            public ISymbol Member { get; }

            public string OperationName { get; }
        }

    }
}
