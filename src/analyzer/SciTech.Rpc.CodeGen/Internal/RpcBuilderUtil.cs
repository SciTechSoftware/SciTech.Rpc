#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using Microsoft.CodeAnalysis;
//using SciTech.Rpc.Client;
//using SciTech.Rpc.Logging;
//using SciTech.Rpc.Serialization;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.FormattableString;

namespace SciTech.Rpc.Internal
{
    public enum RpcMethodType
    {
        Unary,
        ServerStreaming,
        //PropertySet,
        //PropertyGet,
        EventAdd,
        EventRemove
    }

    internal enum ServiceOperationReturnKind
    {
        Standard,
        Service,
        ServiceArray,
        ServiceRef,
        ServiceRefArray
    }

#pragma warning disable CA1815 // Override equals and operator equals on value types
    public struct RpcRequestParameter
#pragma warning restore CA1815 // Override equals and operator equals on value types
    {
        public RpcRequestParameter(ITypeSymbol type, int index)
        {
            this.Type = type ?? throw new ArgumentNullException(nameof(type));
            this.Index = index;
        }

        public int Index { get; }

        public ITypeSymbol Type { get; }
    }

#pragma warning disable CA1815 // Override equals and operator equals on value types
    public class RpcRequestTypeInfo
#pragma warning restore CA1815 // Override equals and operator equals on value types
    {
        public RpcRequestTypeInfo(string type, ImmutableArray<RpcRequestParameter> parameters, ITypeSymbol? callbackRequestType, int? callbackParameterIndex, string? cancellationTokenName)
        {
            this.Type = type ?? throw new ArgumentNullException(nameof(type));
            this.Parameters = parameters;
            this.CallbackRequestType = callbackRequestType;
            this.CallbackParameterIndex = callbackParameterIndex;
            this.CancellationTokenName = cancellationTokenName;
        }

        public int? CallbackParameterIndex { get; }

        public ITypeSymbol? CallbackRequestType { get; }

        public string? CancellationTokenName { get; }

        public ImmutableArray<RpcRequestParameter> Parameters { get; }

        public string Type { get; }

        public bool HasSpecialParameter => this.CallbackParameterIndex != null || this.CancellationTokenName != null;
    }


#pragma warning disable CA1062 // Validate arguments of public methods
    public class RpcBuilderUtil
    {
        private ITypeSymbol? voidSymbol;

        public RpcBuilderUtil(Compilation compilation)
        {
            voidSymbol = compilation.GetSymbolsWithName("System.Void").OfType<ITypeSymbol>().FirstOrDefault();
        }

        /// <summary>
        /// Enumerates all declared RPC members in the service interface specified by <paramref name="serviceInfo"/>.
        /// </summary>
        /// <param name="serviceInfo"></param>
        /// <param name="splitProperties">Indicates that separate <see cref="RpcOperationInfo"/>s should be returned for property get/set 
        /// methods, instead of a single <see cref="RpcPropertyInfo"/>.</param>
        /// <returns></returns>
        // TODO: This method should maybe be moved to RpcServiceInfo, or at least be an RpcServiceInfo extension method.
        public IEnumerable<RpcMemberInfo> EnumOperationHandlers(RpcServiceInfo serviceInfo, bool splitProperties)
        {
            var handledMembers = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

            var members = serviceInfo.Type.GetMembers();
            var events = members.OfType<IEventSymbol>().Where(e => !e.IsStatic && SymbolEqualityComparer.Default.Equals(e.ContainingType, serviceInfo.Type)).ToList();

            foreach (var eventInfo in events)
            {
                if (eventInfo.Type == null)
                {
                    // How could this happen?
                    throw new NotSupportedException($"{eventInfo.Name} has no EventHandlerType");
                }

                if (eventInfo.AddMethod == null || eventInfo.RemoveMethod == null)
                {
                    // How could this happen?
                    throw new NotSupportedException($"{eventInfo.Name} is missing an Add or Remove method.");
                }

                var rpcEventInfo = GetEventInfoFromEvent(serviceInfo, eventInfo);

                handledMembers.Add(eventInfo.AddMethod);
                handledMembers.Add(eventInfo.RemoveMethod);

                yield return rpcEventInfo;
            }

            var properties = members.OfType<IPropertySymbol>().Where(e => !e.IsStatic && SymbolEqualityComparer.Default.Equals(e.ContainingType, serviceInfo.Type)).ToList();

            foreach (var propertyInfo in properties)
            {
                var rpcPropertyInfo = RpcBuilderUtil.GetPropertyInfoFromProperty(serviceInfo, propertyInfo);
                var propertyRpcAttribute = propertyInfo.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToString() == "SciTech.Rpc.RpcOperationAttribute");

                if (propertyInfo.GetMethod != null)
                {
                    if (splitProperties)
                    {
                        var getRpcAttribute = propertyInfo.GetMethod.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToString() == "SciTech.Rpc.RpcOperationAttribute");

                        var getOp = new RpcOperationInfo(
                            service: rpcPropertyInfo.Service,
                            name: $"Get{propertyInfo.Name}",
                            declaringMember: propertyInfo,
                            method: propertyInfo.GetMethod,
                            requestType: "SciTech.Rpc.Internal.RpcObjectRequest",
                            requestParameters: ImmutableArray<RpcRequestParameter>.Empty,
                            callbackParameterIndex: null,
                            cancellationTokenName: null,
                            methodType: RpcMethodType.Unary,
                            isAsync: false,
                            responseType: GetResponseType(RpcMethodType.Unary, rpcPropertyInfo.Property.Type, rpcPropertyInfo.ResponseReturnType),
                            responseReturnType: rpcPropertyInfo.ResponseReturnType,
                            returnType: propertyInfo.Type,
                            returnKind: rpcPropertyInfo.PropertyTypeKind,
                            allowInlineExecution: TryGetAttributeValue(getRpcAttribute, "AllowInlineExecution") as bool? ?? TryGetAttributeValue(propertyRpcAttribute, "AllowInlineExecution") as bool? ?? false,
                            metadata: GetPropertyMethodMetadata(serviceInfo, propertyInfo, propertyInfo.GetMethod)
                            );

                        yield return getOp;
                    }

                    handledMembers.Add(propertyInfo.GetMethod);
                }

                if (propertyInfo.SetMethod != null)
                {
                    if (splitProperties)
                    {
                        var setRpcAttribute = GetSingleAttribute(propertyInfo.SetMethod, "SciTech.Rpc.RpcOperationAttribute");

                        var setOp = new RpcOperationInfo(
                            service: rpcPropertyInfo.Service,
                            name: $"Set{propertyInfo.Name}",
                            declaringMember: propertyInfo,
                            method: propertyInfo.SetMethod,
                            requestType: Invariant($"SciTech.Rpc.RpcObjectRequest<{rpcPropertyInfo.ResponseReturnType}>"),
                            requestParameters: ImmutableArray.Create(new RpcRequestParameter(propertyInfo.Type, 0)),
                            callbackParameterIndex: null,
                            cancellationTokenName: null,
                            methodType: RpcMethodType.Unary,
                            isAsync: false,
                            responseType: "SciTech.Rpc.RpcResponse",
                            returnType: propertyInfo.SetMethod.ReturnType,  // Should be void.
                            responseReturnType: "void",
                            returnKind: ServiceOperationReturnKind.Standard,
                            allowInlineExecution: TryGetAttributeValue(setRpcAttribute, "AllowInlineExecution") as bool? ?? TryGetAttributeValue(propertyRpcAttribute, "AllowInlineExecution") as bool? ?? false,
                            metadata: GetPropertyMethodMetadata(serviceInfo, propertyInfo, propertyInfo.SetMethod)
                        );

                        yield return setOp;
                    }

                    handledMembers.Add(propertyInfo.SetMethod);
                }

                if (!splitProperties)
                {
                    yield return rpcPropertyInfo;
                }
            }

            foreach (var method in serviceInfo.Type.GetMembers().OfType<IMethodSymbol>().Where(s => s.DeclaredAccessibility == Accessibility.Public && !s.IsStatic))
            {
                if (handledMembers.Add(method))
                {
                    var opInfo = GetOperationInfoFromMethod(serviceInfo, method);
                    //this.CheckMethod(opInfo);
                    switch (opInfo.MethodType)
                    {
                        case RpcMethodType.Unary:
                        case RpcMethodType.ServerStreaming:
                            yield return opInfo;
                            break;
                    }
                }
            }

        }


        private static object? TryGetAttributeValue(AttributeData? attribute, string valueName)
        {
            if (attribute != null)
            {
                foreach (var arg in attribute.NamedArguments)
                {
                    if (arg.Key == valueName)
                    {
                        return arg.Value.Value;
                    }
                }
            }

            return null;
        }
        //public static List<RpcServiceInfo> GetAllServices<TService>(bool ignoreUnknownInterfaces)
        //{
        //    return GetAllServices(typeof(TService), RpcServiceDefinitionSide.Both, ignoreUnknownInterfaces);
        //}

        public static List<RpcServiceInfo> GetAllServices(ITypeSymbol serviceType, RpcServiceDefinitionSide serviceDefinitionType, bool ignoreUnknownInterfaces)
        {
            return GetAllServices(serviceType, null, serviceDefinitionType, ignoreUnknownInterfaces);
        }

        public static List<RpcServiceInfo> GetAllServices(ITypeSymbol serviceType, ITypeSymbol? implementationType, RpcServiceDefinitionSide serviceDefinitionType, bool ignoreUnknownInterfaces)
        {
            var allServices = new List<RpcServiceInfo>();
            var declaredServiceInfo = GetServiceInfoFromType(serviceType, implementationType, !ignoreUnknownInterfaces);
            if (declaredServiceInfo != null)
            {
                if (serviceDefinitionType == RpcServiceDefinitionSide.Both
                    || declaredServiceInfo.DefinitionSide == RpcServiceDefinitionSide.Both
                    || serviceDefinitionType == declaredServiceInfo.DefinitionSide)
                {
                    declaredServiceInfo.IsDeclaredService = true;
                    allServices.Add(declaredServiceInfo);
                }
            }

            var interfaces = serviceType.AllInterfaces;

            foreach (var inheritedInterfaceType in interfaces)
            {
                if (IsInterface(inheritedInterfaceType, "SciTech.Rpc.Client.IRpcProxy")
                    || IsInterface(inheritedInterfaceType, "System.IEquatable<SciTech.Rpc.Client.IRpcProxy>")
                    || IsInterface(inheritedInterfaceType, "System.IDisposable"))
                {
                    continue;
                }

                var interfaceServiceInfo = GetServiceInfoFromType(inheritedInterfaceType, implementationType, !ignoreUnknownInterfaces);
                if (interfaceServiceInfo != null)
                {
                    if (serviceDefinitionType == RpcServiceDefinitionSide.Both
                        || interfaceServiceInfo.DefinitionSide == RpcServiceDefinitionSide.Both
                        || serviceDefinitionType == interfaceServiceInfo.DefinitionSide)

                    {
                        allServices.Add(interfaceServiceInfo);
                    }
                }
            }

            return allServices;
        }

        public static RpcEventInfo GetEventInfoFromEvent(RpcServiceInfo serviceInfo, IEventSymbol eventInfo)
        {
            ITypeSymbol? eventHandlerType = eventInfo.Type ?? throw new NotSupportedException($"{eventInfo.Name} has no EventHandlerType"); ;
            string eventArgsType;
            if (eventHandlerType is INamedTypeSymbol namedType && namedType.IsGenericType)
            {
                var eventHandlerGenericType = namedType.ConstructUnboundGenericType();

                if (eventHandlerGenericType != typeof(EventHandler<>))
                {
                    throw new RpcDefinitionException("Event handler must be EventHandler, or EventHandler<>.");
                }

                eventArgsType = namedType.TypeArguments[0].ToDisplayString();
            }
            else
            {
                if (eventHandlerType != typeof(EventHandler))
                {
                    throw new RpcDefinitionException("Event handler must be EventHandler, or EventHandler<>.");
                }

                eventArgsType = "System.EventArgs";
            }

            return new RpcEventInfo
            (
                service: serviceInfo,
                eventInfo: eventInfo,
                eventArgsType: eventArgsType,
                metadata: GetEventMetadata(serviceInfo, eventInfo)
            );
        }

        public RpcOperationInfo GetOperationInfoFromMethod(RpcServiceInfo serviceInfo, IMethodSymbol method)
        {
            var parameters = method.Parameters;

            var requestTypeInfo = GetRequestType(parameters, serviceInfo.IsSingleton);

            ITypeSymbol actualReturnType = method.ReturnType;
            bool isAsync = false;
            RpcMethodType methodType = RpcMethodType.Unary;
            if (method.ReturnType is INamedTypeSymbol namedType && namedType.IsGenericType)
            {
                var genericTypeDef = namedType.ConstructUnboundGenericType();
                if (genericTypeDef.Equals(typeof(IAsyncEnumerable<>)))// || genericTypeDef.Equals(typeof(IAsyncEnumerator<>)))
                {
                    if (requestTypeInfo.CallbackParameterIndex != null)
                    {
                        throw new RpcDefinitionException($"A streaming server method '{method.Name}' cannot have a callback parameter.");
                    }
                    actualReturnType = namedType.TypeArguments[0];
                    methodType = RpcMethodType.ServerStreaming;
                    isAsync = true;
                }
                else if (genericTypeDef.Equals(typeof(Task<>)) || genericTypeDef.Equals(typeof(ValueTask<>)))
                {
                    actualReturnType = namedType.TypeArguments[0];
                    isAsync = true;
                }
            }
            else if (IsClass(method.ReturnType, "System.Threading.Tasks.Task"))
            {
                actualReturnType = null; // voidSymbol "void";
                isAsync = true;
            }

            if (requestTypeInfo.CallbackParameterIndex != null)
            {
                if (actualReturnType != null)//voidSymbol )//"void")
                {
                    throw new RpcDefinitionException($"Method '{method.Name}' has a callback parameter and must return void.");
                }

                actualReturnType = requestTypeInfo.CallbackRequestType ?? throw new InvalidOperationException("CallbackRequestType must be initialized when CallbackParameterIndex is not null");
                methodType = RpcMethodType.ServerStreaming;
            }

            string? operationName = null;

            var rpcAttribute = GetSingleAttribute(method, "SciTech.Rpc.RpcOperationAttribute");
            if (rpcAttribute != null)
            {
                operationName = TryGetAttributeValue(rpcAttribute, "Name") as string;
            }

            if (string.IsNullOrEmpty(operationName))
            {
                operationName = method.Name;
                if (isAsync && operationName.EndsWith("Async", StringComparison.Ordinal))
                {
                    operationName = operationName.Substring(0, operationName.Length - "Async".Length);
                }
            }

            var (returnKind, responseReturnType) = GetOperationReturnKind(actualReturnType);
            string responseType = GetResponseType(methodType, actualReturnType, responseReturnType);

            ImmutableArray<object> metadata = GetMethodMetadata(serviceInfo, method);

            return new RpcOperationInfo
            (
                service: serviceInfo,
                method: method,
                declaringMember: method,
                methodType: methodType,
                isAsync: isAsync,
                name: operationName!,
                requestParameters: requestTypeInfo.Parameters,
                callbackParameterIndex: requestTypeInfo.CallbackParameterIndex,
                cancellationTokenName: requestTypeInfo.CancellationTokenName,
                requestType: requestTypeInfo.Type,
                returnType: actualReturnType,
                responseType: responseType,
                responseReturnType: responseReturnType,
                returnKind: returnKind,
                allowInlineExecution: (TryGetAttributeValue(rpcAttribute, "AllowInlineExecution") as bool?) ?? false,
                metadata: metadata
            );
        }

        public static RpcPropertyInfo GetPropertyInfoFromProperty(RpcServiceInfo serviceInfo, IPropertySymbol propertyInfo)
        {
            var propertyType = propertyInfo.Type;
            var (returnKind, responseReturnType) = GetOperationReturnKind(propertyType);
            var propertyRpcAttribute = propertyInfo.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToString() == "SciTech.Rpc.RpcOperationAttribute");

            RpcOperationInfo? getOp = null;
            RpcOperationInfo? setOp = null;

            if (propertyInfo.GetMethod != null)
            {
                var getRpcAttribute = propertyInfo.GetMethod.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToString() == "SciTech.Rpc.RpcOperationAttribute");

                getOp = new RpcOperationInfo(
                    service: serviceInfo,
                    name: $"Get{propertyInfo.Name}",
                    declaringMember: propertyInfo,
                    method: propertyInfo.GetMethod,
                    requestType: "SciTech.Rpc.Internal.RpcObjectRequest",
                    requestParameters: ImmutableArray<RpcRequestParameter>.Empty,
                    callbackParameterIndex: null,
                    cancellationTokenName: null,
                    methodType: RpcMethodType.Unary,
                    isAsync: false,
                    responseType: GetResponseType(RpcMethodType.Unary, propertyInfo.Type, responseReturnType),
                    responseReturnType: responseReturnType,
                    returnType: propertyInfo.Type,
                    returnKind: returnKind,
                    allowInlineExecution: TryGetAttributeValue(getRpcAttribute, "AllowInlineExecution") as bool? ?? TryGetAttributeValue(propertyRpcAttribute, "AllowInlineExecution") as bool? ?? false,
                    metadata: GetPropertyMethodMetadata(serviceInfo, propertyInfo, propertyInfo.GetMethod)
                    );

            }

            if (propertyInfo.SetMethod != null)
            {
                var setRpcAttribute = GetSingleAttribute(propertyInfo.SetMethod, "SciTech.Rpc.RpcOperationAttribute");

                setOp = new RpcOperationInfo(
                    service: serviceInfo,
                    name: $"Set{propertyInfo.Name}",
                    declaringMember: propertyInfo,
                    method: propertyInfo.SetMethod,
                    requestType: Invariant($"SciTech.Rpc.RpcObjectRequest<{responseReturnType}>"),
                    requestParameters: ImmutableArray.Create(new RpcRequestParameter(propertyInfo.Type, 0)),
                    callbackParameterIndex: null,
                    cancellationTokenName: null,
                    methodType: RpcMethodType.Unary,
                    isAsync: false,
                    responseType: "SciTech.Rpc.RpcResponse",
                    returnType: propertyInfo.SetMethod.ReturnType,  // Should be void.
                    responseReturnType: "void",
                    returnKind: ServiceOperationReturnKind.Standard,
                    allowInlineExecution: TryGetAttributeValue(setRpcAttribute, "AllowInlineExecution") as bool? ?? TryGetAttributeValue(propertyRpcAttribute, "AllowInlineExecution") as bool? ?? false,
                    metadata: GetPropertyMethodMetadata(serviceInfo, propertyInfo, propertyInfo.SetMethod)
                );
            }

            return new RpcPropertyInfo
            (
                service: serviceInfo,
                propertyInfo: propertyInfo,
                propertyTypeKind: returnKind,
                responseReturnType: responseReturnType,
                getOp: getOp,
                setOp: setOp,
                metadata: GetPropertyMetadata(serviceInfo, propertyInfo)
            );
        }

        internal static bool IsType(ISymbol symbol, string className, TypeKind typeKind)
        {
            if (symbol is INamedTypeSymbol typeSymbol)
            {
                if (typeSymbol.TypeKind == typeKind && typeSymbol.ToString() == className)
                {
                    return true;
                }
            }

            return false;
        }
        internal static bool IsClass(ISymbol symbol, string className)
            => IsType(symbol, className, TypeKind.Class);

        internal static bool IsGenericType(ISymbol symbol, string className, TypeKind typeKind)
        {
            return symbol is INamedTypeSymbol namedType && IsType(namedType.ConstructUnboundGenericType(), className, typeKind);
        }


        private static bool IsInterface(ISymbol symbol, string className)
            => IsType(symbol, className, TypeKind.Interface);

        private static bool IsSubClass(ISymbol symbol, string className)
        {
            var currSymbol = symbol;
            while (currSymbol != null)
            {
                if (IsClass(currSymbol, className)) return true;

                currSymbol = (currSymbol as ITypeSymbol)?.BaseType;
            }

            return false;
        }
        private static bool IsValueType(ISymbol symbol, string typeName)
            => IsType(symbol, typeName, TypeKind.Struct);

        public RpcRequestTypeInfo GetRequestType(ImmutableArray<IParameterSymbol> parameters, bool isSingleton)
        {
            string? cancellationTokenName = null;
            ITypeSymbol? callbackRequestType = null;
            int? callbackParameterIndex = null;

            var parametersBuilder = ImmutableArray.CreateBuilder<RpcRequestParameter>(parameters.Length);
            var parameterTypesList = new List<ITypeSymbol>(parameters.Length);
            for (int parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
            {
                var parameterInfo = parameters[parameterIndex];

                // Handle special parameters.
                if (IsValueType(parameterInfo.Type, "System.Threading.CancellationToken"))
                {
                    if (cancellationTokenName != null)
                    {
                        throw new RpcDefinitionException("RPC operation can only include a single CancellationToken.");
                    }
                    cancellationTokenName = parameterInfo.Name;
                }
                else if (IsSubClass(parameterInfo.Type, "System.Delegate"))
                {
                    if (callbackParameterIndex != null)
                    {
                        throw new RpcDefinitionException("RPC operation can only include a single callback.");
                    }

                    var invokeMethod = parameterInfo.Type.GetMembers().OfType<IMethodSymbol>().FirstOrDefault(m => m.Name == "Invoke") ?? throw new RpcDefinitionException("Missing invoke method on callback.");

                    if (SymbolEqualityComparer.Default.Equals(voidSymbol, invokeMethod.ReturnType))
                    {
                        throw new RpcDefinitionException("Callback must return void.");
                    }

                    var callbackRequestTypeInfo = GetRequestType(invokeMethod.Parameters, true);
                    if (callbackRequestTypeInfo.HasSpecialParameter)
                    {
                        throw new RpcDefinitionException("Callback must not have an special parameters (e.g. CancellationToken, call contexts, or callback.");
                    }

                    if (callbackRequestTypeInfo.Parameters.Length != 1)
                    {
                        throw new RpcDefinitionException("Callbacks can currently only use a single parameter.");
                    }

                    if (IsGenericType(parameterInfo.Type, "System.Action<>", TypeKind.Class))
                    {
                        throw new RpcDefinitionException("Callback currently only support Action<>.");
                    }

                    callbackRequestType = callbackRequestTypeInfo.Parameters[0].Type;
                    if (callbackRequestType.IsValueType)
                    {
                        throw new RpcDefinitionException("Callback currently does not support value types.");
                    }

                    callbackParameterIndex = parameterIndex;
                }
                else
                {
                    parametersBuilder.Add(new RpcRequestParameter(parameterInfo.Type, parameterIndex));
                    parameterTypesList.Add(parameterInfo.Type);
                }
            }

            parametersBuilder.Capacity = parametersBuilder.Count;
            var requestParameters = parametersBuilder.MoveToImmutable();

            string requestType;
            string baseRequestType = isSingleton ? "SciTech.Internal.RpcRequest" : "SciTech.Internal.RpcObjectRequest";
            if (parameterTypesList.Count > 0)
            {
                StringBuilder requestBuilder = new StringBuilder(baseRequestType);
                requestBuilder.Append("<");
                foreach (var param in parameterTypesList)
                {
                    requestBuilder.Append(param.ToDisplayString());
                }
                requestBuilder.Append(">");
                requestType = requestBuilder.ToString();
            }
            else
            {
                requestType = baseRequestType;
            }
            //var parameterTypes = parameterTypesList.ToArray();
            //if (isSingleton)
            //{
            //    switch (parameterTypes.Length)
            //    {
            //        case 0:
            //            requestType = typeof(RpcRequest);
            //            break;
            //        case 1:
            //            requestType = typeof(RpcRequest<>).MakeGenericType(parameterTypes);
            //            break;
            //        case 2:
            //            requestType = typeof(RpcRequest<,>).MakeGenericType(parameterTypes);
            //            break;
            //        case 3:
            //            requestType = typeof(RpcRequest<,,>).MakeGenericType(parameterTypes);
            //            break;
            //        case 4:
            //            requestType = typeof(RpcRequest<,,,>).MakeGenericType(parameterTypes);
            //            break;
            //        case 5:
            //            requestType = typeof(RpcRequest<,,,,>).MakeGenericType(parameterTypes);
            //            break;
            //        case 6:
            //            requestType = typeof(RpcRequest<,,,,,>).MakeGenericType(parameterTypes);
            //            break;
            //        case 7:
            //            requestType = typeof(RpcRequest<,,,,,,>).MakeGenericType(parameterTypes);
            //            break;
            //        case 8:
            //            requestType = typeof(RpcRequest<,,,,,,,>).MakeGenericType(parameterTypes);
            //            break;
            //        case 9:
            //            requestType = typeof(RpcRequest<,,,,,,,,>).MakeGenericType(parameterTypes);
            //            break;
            //        default:
            //            throw new NotImplementedException("An RPC operation is currently limited to 9 parameters.");
            //    }
            //}
            //else
            //{
            //    switch (parameterTypes.Length)
            //    {
            //        case 0:
            //            requestType = typeof(RpcObjectRequest);
            //            break;
            //        case 1:
            //            requestType = typeof(RpcObjectRequest<>).MakeGenericType(parameterTypes);
            //            break;
            //        case 2:
            //            requestType = typeof(RpcObjectRequest<,>).MakeGenericType(parameterTypes);
            //            break;
            //        case 3:
            //            requestType = typeof(RpcObjectRequest<,,>).MakeGenericType(parameterTypes);
            //            break;
            //        case 4:
            //            requestType = typeof(RpcObjectRequest<,,,>).MakeGenericType(parameterTypes);
            //            break;
            //        case 5:
            //            requestType = typeof(RpcObjectRequest<,,,,>).MakeGenericType(parameterTypes);
            //            break;
            //        case 6:
            //            requestType = typeof(RpcObjectRequest<,,,,,>).MakeGenericType(parameterTypes);
            //            break;
            //        case 7:
            //            requestType = typeof(RpcObjectRequest<,,,,,,>).MakeGenericType(parameterTypes);
            //            break;
            //        case 8:
            //            requestType = typeof(RpcObjectRequest<,,,,,,,>).MakeGenericType(parameterTypes);
            //            break;
            //        case 9:
            //            requestType = typeof(RpcObjectRequest<,,,,,,,,>).MakeGenericType(parameterTypes);
            //            break;
            //        default:
            //            throw new NotImplementedException("An RPC operation is currently limited to 9 parameters.");
            //    }
            //}

            return new RpcRequestTypeInfo(requestType, requestParameters, callbackRequestType, callbackParameterIndex, cancellationTokenName);

        }

        public static RpcServiceInfo GetServiceInfoFromType(ITypeSymbol serviceType, ITypeSymbol? implementationType = null)
        {
            return GetServiceInfoFromType(serviceType, implementationType, true)!;
        }

        public static RpcServiceInfo? TryGetServiceInfoFromType(ITypeSymbol serviceType, ITypeSymbol? implementationType = null)
        {
            return GetServiceInfoFromType(serviceType, implementationType, false);
        }

        internal static List<RpcServiceInfo> GetAllServices(ITypeSymbol serviceType, bool ignoreUnknownInterfaces)
        {
            return GetAllServices(serviceType, RpcServiceDefinitionSide.Both, ignoreUnknownInterfaces);
        }
        static AttributeData? GetSingleAttribute(ISymbol symbol, string attributeName)
        {
            return symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToString() == attributeName /*&& assembly*/);

        }

        internal static (ServiceOperationReturnKind, string) GetOperationReturnKind(ITypeSymbol returnType)
        {
            // TODO: Should use a [return:SerializeAs] attribute as well.
            var rpcAttribute = GetSingleAttribute(returnType, "SciTech.Rpc.RpcServiceAttribute");
            if (rpcAttribute != null)
            {
                var responseReturnType = "SciTech.Rpc.RpcObjectRef";
                return (ServiceOperationReturnKind.Service, responseReturnType);
            }
            else
            {
                if (IsSubClass(returnType, "SciTech.Rpc.RpcObjectRef"))
                {
                    var responseReturnType = "SciTech.Rpc.RpcObjectRef";
                    return (ServiceOperationReturnKind.ServiceRef, responseReturnType);
                }
            }
            if (returnType is IArrayTypeSymbol arrayTypeSymbol)
            {
                var elementType = arrayTypeSymbol.ElementType;
                if (elementType != null)
                {
                    var elementRpcAttribute = GetSingleAttribute(returnType, "SciTech.Rpc.RpcServiceAttribute");
                    if (elementRpcAttribute != null)
                    {
                        var responseReturnType = "SciTech.Rpc.RpcObjectRef[]";
                        return (ServiceOperationReturnKind.ServiceArray, responseReturnType);
                    }
                    else
                    {
                        if (IsSubClass(elementType, "SciTech.Rpc.RpcObjectRef"))
                        {
                            var responseReturnType = "SciTech.Rpc.RpcObjectRef[]";
                            return (ServiceOperationReturnKind.ServiceRefArray, responseReturnType);
                        }
                    }
                }
            }

            return (ServiceOperationReturnKind.Standard, returnType.ToDisplayString());
        }

        internal static bool IsRpcServiceType(Type serviceType)
        {
            var rpcAttribute = serviceType.GetCustomAttribute<RpcServiceAttribute>();
            return rpcAttribute != null;
        }

        internal static string RetrieveFaultCode(ITypeSymbol faultType)
        {
            if (faultType == null)
            {
                return "";
            }

            var detailsAttribute = GetSingleAttribute(faultType, "SciTech.Rpc.RpcFaultDetailsAttribute");
            string? faultCode = TryGetAttributeValue(detailsAttribute, "FaultCode") as string;
            if (!string.IsNullOrEmpty(faultCode))
            {
                return faultCode!;
            }

            return faultType.Name;
        }

        private static string GetDefaultServiceName(ITypeSymbol serviceType)
        {
            string serviceName = serviceType.Name;
            if (serviceName.StartsWith("I", StringComparison.Ordinal) && serviceName.Length > 1 && char.IsUpper(serviceName[1]))
            {
                serviceName = serviceName.Substring(1);
            }

            return serviceName;
        }

        private static ImmutableArray<object> GetEventMetadata(RpcServiceInfo serviceInfo, IEventSymbol eventInfo)
        {
            var metadataBuilder = ImmutableArray.CreateBuilder<object>();
            metadataBuilder.AddRange(serviceInfo.Metadata);

            metadataBuilder.AddRange(eventInfo.GetAttributes());

            var implMethod = GetImplementationMethod(serviceInfo, eventInfo.AddMethod);
            if (implMethod != null)
            {
                var implProperty = serviceInfo.ImplementationType!.GetMembers().OfType<IEventSymbol>().Where(s => !s.IsStatic)
                    .FirstOrDefault(p => SymbolEqualityComparer.Default.Equals(p.AddMethod, implMethod));

                if (implProperty != null)
                {
                    metadataBuilder.AddRange(implProperty.GetAttributes());
                }
            }

            var metadata = metadataBuilder.ToImmutableArray();
            return metadata;
        }

        private static ImmutableArray<object> GetMethodMetadata(RpcServiceInfo serviceInfo, IMethodSymbol method)
        {
            var metadataBuilder = ImmutableArray.CreateBuilder<object>();
            metadataBuilder.AddRange(serviceInfo.Metadata);

            ITypeSymbol? implementationType = serviceInfo.ImplementationType;
            IMethodSymbol? implMethod = GetImplementationMethod(serviceInfo, method);

            if (implMethod != null)
            {
                metadataBuilder.AddRange(GetInheritedAttributes(implMethod));
            }
            else
            {
                metadataBuilder.AddRange(GetInheritedAttributes(method));
            }

            var metadata = metadataBuilder.ToImmutableArray();
            return metadata;
        }

        private static IMethodSymbol? GetImplementationMethod(RpcServiceInfo serviceInfo, ISymbol? method)
        {
            ITypeSymbol? implementationType = serviceInfo.ImplementationType;
            if (implementationType != null && method != null)
            {
                return implementationType.FindImplementationForInterfaceMember(method) as IMethodSymbol;
            }

            return null;
        }

        private static ImmutableArray<object> GetPropertyMetadata(RpcServiceInfo serviceInfo, IPropertySymbol property)
        {
            var metadataBuilder = ImmutableArray.CreateBuilder<object>();
            metadataBuilder.AddRange(serviceInfo.Metadata);

            metadataBuilder.AddRange(property.GetAttributes());

            ITypeSymbol? implementationType = serviceInfo.ImplementationType;
            if (implementationType != null)
            {

                var implMethod =
                    GetImplementationMethod(serviceInfo, property.GetMethod)
                    ?? GetImplementationMethod(serviceInfo, property.SetMethod);

                if (implMethod != null)
                {
                    var implProperty = implementationType.GetMembers().OfType<IPropertySymbol>()
                        .FirstOrDefault(p =>
                            !p.IsStatic
                            && (SymbolEqualityComparer.Default.Equals(p.GetMethod, implMethod)
                                || SymbolEqualityComparer.Default.Equals(p.SetMethod, implMethod)));

                    if (implProperty != null)
                    {
                        metadataBuilder.AddRange(implProperty.GetAttributes());
                    }
                }
            }

            var metadata = metadataBuilder.ToImmutableArray();
            return metadata;
        }

        private static ImmutableArray<object> GetPropertyMethodMetadata(RpcServiceInfo serviceInfo, IPropertySymbol property, IMethodSymbol method)
        {
            var metadataBuilder = ImmutableArray.CreateBuilder<object>();
            metadataBuilder.AddRange(serviceInfo.Metadata);

            metadataBuilder.AddRange(property.GetAttributes());

            ITypeSymbol? implementationType = serviceInfo.ImplementationType;
            if (implementationType != null)
            {
                var implMethod = GetImplementationMethod(serviceInfo, method);

                if (implMethod != null)
                {
                    var implProperty = implementationType.GetMembers().OfType<IPropertySymbol>()
                        .FirstOrDefault(p =>
                            !p.IsStatic
                            && (SymbolEqualityComparer.Default.Equals(p.GetMethod, implMethod)
                                || SymbolEqualityComparer.Default.Equals(p.SetMethod, implMethod)));

                    if (implProperty != null)
                    {
                        metadataBuilder.AddRange(implProperty.GetAttributes());
                    }

                    metadataBuilder.AddRange(GetInheritedAttributes(implMethod));
                }
            }
            else
            {
                // Assuming that inherit will find attributes on interface method.
                // TODO: Must check!
                metadataBuilder.AddRange(GetInheritedAttributes(method));
            }

            var metadata = metadataBuilder.ToImmutableArray();
            return metadata;
        }

        private static string GetResponseType(RpcMethodType methodType, ITypeSymbol declaredType, string responseReturnType)
        {
            if (methodType == RpcMethodType.ServerStreaming)
            {
                return responseReturnType;
            }

            if (declaredType.SpecialType == SpecialType.System_Void)
            {
                return "SciTech.Rpc.RpcResponse";
            }

            return Invariant($"SciTech.RpcResponse<{responseReturnType}>");
        }

        private static RpcServiceAttributeData? GetRpcServiceAttribute(ITypeSymbol serviceType)
        {
            var attributeData = GetSingleAttribute(serviceType, "SciTech.Rpc.RpcServiceAttribute");
            return attributeData != null ? new RpcServiceAttributeData(attributeData) : null;
            //            RpcServiceAttribute? rpcAttribute = null;
            //#pragma warning disable CA1031 // Do not catch general exception types
            //            try
            //            {
            //                rpcAttribute = serviceType.GetCustomAttribute<RpcServiceAttribute>();
            //            }
            //            catch (Exception )
            //            {
            //                // TODO: Logger.Warn(e, "Failed to retrive RpcServiceAttribute for '{type}'", serviceType);
            //            }
            //#pragma warning restore CA1031 // Do not catch general exception types

            //            return rpcAttribute;
        }

        private static AttributeData? GetServiceContractAttribute(ITypeSymbol serviceType)
        {
            return GetSingleAttribute(serviceType, "System.ServiceModel.ServiceContractAttribute");
        }

        private static List<AttributeData> GetInheritedAttributes(ISymbol symbol)
        {
            var attributes = new List<AttributeData>();
            attributes.AddRange(symbol.GetAttributes());
            return attributes;
        }

        private static string GetNamespace(ISymbol symbol)
        {
            return symbol.ContainingNamespace?.Name ?? "";
        }

        private static RpcServiceInfo GetServiceInfoFromContractAttribute(ITypeSymbol serviceType, ITypeSymbol? implementationType, AttributeData contractAttribute)
        {
            string serviceName;
            string serviceNamespace;

            serviceName = GetServiceName(serviceType, contractAttribute);
            serviceNamespace = GetNamespace(serviceType);

            var metadata = new List<object>();
            metadata.AddRange(GetInheritedAttributes(serviceType));
            if (implementationType != null)
            {
                metadata.AddRange(GetInheritedAttributes(implementationType));
            }

            return new RpcServiceInfo
            (
                type: serviceType,
                @namespace: serviceNamespace,
                name: serviceName,
                definitionSide: RpcServiceDefinitionSide.Both,
                serverType: null,
                implementationType: implementationType,
                isSingleton: false,
                metadata: metadata.ToImmutableArray()
            );
        }

        private static RpcServiceInfo GetServiceInfoFromRpcAttribute(ITypeSymbol serviceType, ITypeSymbol? implementationType, RpcServiceAttributeData rpcAttribute)
        {
            string serviceName;
            string serviceNamespace;

            // Try to retrieve it from the server side definition
            if( rpcAttribute.ServerDefinitionType is ITypeSymbol definitionType)
            {
                if (GetRpcServiceAttribute(definitionType) is RpcServiceAttributeData serverRpcAttribute)
                {
                    serviceName = GetServiceName(definitionType, serverRpcAttribute);
                    serviceNamespace = GetServiceNamespace(definitionType, serverRpcAttribute);
                }
                else if (GetServiceContractAttribute(definitionType) is AttributeData contractAttribute)
                {
                    serviceName = GetServiceName(definitionType, contractAttribute);
                    serviceNamespace = GetNamespace(definitionType);
                }
                else
                {
                    throw new RpcDefinitionException("Server side definition interface must be tagged with the RpcService or ServiceContract attribute.");
                }
            }
            else
            {
                serviceName = GetServiceName(serviceType, rpcAttribute);
                serviceNamespace = GetServiceNamespace(serviceType, rpcAttribute);
            }

            if (!string.IsNullOrEmpty(rpcAttribute.Name) && rpcAttribute.Name != serviceName)
            {
                throw new RpcDefinitionException("Name of server side type does not match specified service name."); ;
            }

            if (!string.IsNullOrEmpty(rpcAttribute.Namespace) && rpcAttribute.Namespace != serviceNamespace)
            {
                throw new RpcDefinitionException("Namespace of server side type does not match specified service namespace."); ;
            }


            var definitionSide = rpcAttribute.ServiceDefinitionSide;

            var metadata = new List<object>();
            metadata.AddRange(GetInheritedAttributes(serviceType));
            if (implementationType != null)
            {
                metadata.AddRange(GetInheritedAttributes(implementationType));
            }

            return new RpcServiceInfo
            (
                type: serviceType,
                @namespace: serviceNamespace,
                name: serviceName,
                definitionSide: definitionSide,
                serverType: rpcAttribute.ServerDefinitionType as ITypeSymbol,
                implementationType: implementationType,
                isSingleton: rpcAttribute.IsSingleton,
                metadata: metadata.ToImmutableArray()
            );
        }

        private static RpcServiceInfo? GetServiceInfoFromType(ITypeSymbol serviceType, ITypeSymbol? implementationType, bool throwIfNotServiceType)
        {
            if (serviceType.TypeKind == TypeKind.Interface)
            {
                RpcServiceAttributeData? rpcAttribute = GetRpcServiceAttribute(serviceType);

                if (rpcAttribute != null)
                {
                    return GetServiceInfoFromRpcAttribute(serviceType, implementationType, rpcAttribute);
                }

                // Let's try with a ServiceContract attribute
                AttributeData? contractAttribute = GetServiceContractAttribute(serviceType);
                if (contractAttribute != null)
                {
                    return GetServiceInfoFromContractAttribute(serviceType, implementationType, contractAttribute);
                }

                if (throwIfNotServiceType)
                {
                    // The RpcService attribute is actually not strictly necessary, but I think
                    // it's good to show the intention that an interface should be used as an RPC interface.
                    throw new ArgumentException("Interface must be tagged with the RpcService or ServiceContract attribute to allow RPC proxy/stub to be generated.");
                }
            }
            else if (throwIfNotServiceType)
            {
                throw new ArgumentException("Service type must be an interface to allow RPC proxy/stub to be generated.");
            }

            return null;
        }

        private static string GetServiceName(ITypeSymbol serviceType, RpcServiceAttributeData? rpcAttribute)
        {
            var serviceName = rpcAttribute?.Name;
            if (string.IsNullOrEmpty(serviceName))
            {
                serviceName = GetDefaultServiceName(serviceType);
            }

            return serviceName!;
        }

        private static string GetServiceName(ITypeSymbol serviceType, AttributeData? contractAttribute)
        {
            var serviceName = contractAttribute?.GetType().GetProperty("Name")?.GetValue(contractAttribute) as string;
            if (string.IsNullOrEmpty(serviceName))
            {
                serviceName = GetDefaultServiceName(serviceType);
            }

            return serviceName!;
        }

        private static string GetServiceNamespace(ITypeSymbol serviceType, RpcServiceAttributeData? rpcAttribute)
        {
            var serviceNamespace = rpcAttribute?.Namespace;
            if (string.IsNullOrEmpty(serviceNamespace) && serviceType is INamedTypeSymbol namedType)
            {
                serviceNamespace = namedType.ContainingNamespace?.Name ?? "";
            }

            return serviceNamespace!;
        }

        internal sealed class RpcServiceAttributeData
        {
            private readonly AttributeData attributeData;

            internal RpcServiceAttributeData(AttributeData attributeData)
            {
                this.attributeData = attributeData;
            }

            private RpcServiceDefinitionSide? serviceDefinitionType;

            /// <summary>
            /// Indicates that this service will always be published as a singleton. It cannot be associated 
            /// with an object id.
            /// </summary>
            public bool IsSingleton => (RpcBuilderUtil.TryGetAttributeValue(this.attributeData, nameof(IsSingleton)) as bool? ) ?? false;

            /// <summary>
            /// Gets the name of this RPC service. If not specified, the name will be retrieved from the <see cref="ServerDefinitionType"/> if available. Otherwise, 
            /// the name of the service interface with the initial 'I' removed will be used.
            /// </summary>
            public string Name => (RpcBuilderUtil.TryGetAttributeValue(this.attributeData, nameof(Name)) as string) ?? "";

            /// <summary>
            /// Gets the namespace of this service. This corresponds to the package name of a gRPC service. If not specified, the name 
            /// will be retrieved from the <see cref="ServerDefinitionType"/> if available. Otherwise, 
            /// the namespace of the service interface will be used.
            /// </summary>
            public string Namespace => (RpcBuilderUtil.TryGetAttributeValue(this.attributeData, nameof(Namespace)) as string) ?? "";

            /// <summary>
            /// Optional information about the server side definition type, used when <see cref="ServiceDefinitionSide"/> is 
            /// <see cref="RpcServiceDefinitionSide.Client"/>. Specifying this type allows RPC analyzer and runtime code generation to validate 
            /// that the client definition matches the server definition. It also allows service name and similar properties to be retrieved
            /// from the server side definition.
            /// </summary>
            public object? ServerDefinitionType => RpcBuilderUtil.TryGetAttributeValue(this.attributeData, nameof(ServerDefinitionType));

            /// <summary>
            /// Optional information about the type name of the server side definition type. This property can be used instead of <see cref="ServerDefinitionType"/> 
            /// when the server definition type is not available from the client definition assembly. 
            /// </summary>
            /// <remarks>
            /// If both <see cref="ServerDefinitionType"/>  and <see cref="ServerDefinitionTypeName"/> are specified, then <see cref="ServerDefinitionType"/> takes precedence.
            /// Specifying this type name allows the RPC analyzer to validate that the client definition matches the server definition. This
            /// type name is not used by the runtime code generator.
            /// </remarks>
            public string ServerDefinitionTypeName => (RpcBuilderUtil.TryGetAttributeValue(this.attributeData, nameof(ServerDefinitionTypeName)) as string) ?? "";

            /// <summary>
            /// Indicates whether the service interface defines the server side, client side, or both sides of the RPC service. If <see cref="ServerDefinitionType"/>
            /// or <see cref="ServerDefinitionTypeName"/> is specified, this property will be <see cref="RpcServiceDefinitionSide.Client"/> by default; 
            /// otherwise it will be <see cref="RpcServiceDefinitionSide.Both"/>.
            /// </summary>
            public RpcServiceDefinitionSide ServiceDefinitionSide
            {
                get => this.serviceDefinitionType
                    ?? (this.ServerDefinitionType != null || !string.IsNullOrEmpty(this.ServerDefinitionTypeName)
                    ? RpcServiceDefinitionSide.Client : RpcServiceDefinitionSide.Both);
            }
        }
    }
    
    #pragma warning restore CA1062 // Validate arguments of public methods

    public class RpcEventInfo : RpcMemberInfo
    {
        public RpcEventInfo(RpcServiceInfo service, IEventSymbol eventInfo, string eventArgsType, ImmutableArray<object> metadata)
            : base(eventInfo?.Name!, service, eventInfo!, metadata)
        {
            this.Event = eventInfo ?? throw new ArgumentNullException(nameof(eventInfo));
            this.EventArgsType = eventArgsType ?? throw new ArgumentNullException(nameof(eventArgsType));
        }

        public IEventSymbol Event { get; }

        public string EventArgsType { get; }

    }

    public class RpcMemberInfo
    {
        public RpcMemberInfo(string name, RpcServiceInfo service, ISymbol declaringMember, ImmutableArray<object> metadata)
        {
            this.Name = name ?? throw new ArgumentNullException(nameof(name));
            this.Service = service ?? throw new ArgumentNullException(nameof(service));
            this.DeclaringMember = declaringMember ?? throw new ArgumentNullException(nameof(declaringMember));
            this.Metadata = metadata.IsDefault ? ImmutableArray<object>.Empty : metadata;
        }

        public ISymbol DeclaringMember { get; }

        public string FullName => $"{this.Service.FullName}.{this.Name}";

        public string FullServiceName => this.Service?.FullName ?? "";

        public ImmutableArray<object> Metadata { get; }

        public string Name { get; }

        public RpcServiceInfo Service { get; }
    }

    public class RpcOperationInfo : RpcMemberInfo
    {
        internal RpcOperationInfo(string name,
            RpcServiceInfo service,
            ISymbol declaringMember,
            IMethodSymbol method,
            RpcMethodType methodType,
            bool isAsync,
            ImmutableArray<RpcRequestParameter> requestParameters,
            int? callbackParameterIndex,
            string? cancellationTokenName,
            ITypeSymbol returnType,
            string responseReturnType,
            string requestType,
            string responseType,
            ServiceOperationReturnKind returnKind,
            bool allowInlineExecution,
            ImmutableArray<object> metadata) :
            base(name, service, declaringMember, metadata)
        {
            this.Method = method ?? throw new ArgumentNullException(nameof(method));
            this.MethodType = methodType;
            this.IsAsync = isAsync;
            this.RequestParameters = !requestParameters.IsDefault ? requestParameters : throw new ArgumentNullException(nameof(requestParameters));
            this.CallbackParameterIndex = callbackParameterIndex;
            this.CancellationTokenName = !string.IsNullOrEmpty(cancellationTokenName) ? cancellationTokenName : "default";
            this.ReturnType = returnType ?? throw new ArgumentNullException(nameof(returnType));
            this.ResponseReturnType = responseReturnType ?? throw new ArgumentNullException(nameof(responseReturnType));
            this.RequestType = requestType ?? throw new ArgumentNullException(nameof(requestType));
            this.ResponseType = responseType ?? throw new ArgumentNullException(nameof(responseType));
            this.ReturnKind = returnKind;
            this.AllowInlineExecution = allowInlineExecution;
        }

        public bool AllowInlineExecution { get; }

        public string  CancellationTokenName { get; }

        public int? CallbackParameterIndex { get; }

        public bool IsAsync { get; }

        public IMethodSymbol Method { get; }

        public RpcMethodType MethodType { get; }

        public ImmutableArray<RpcRequestParameter> RequestParameters { get; }

        public string RequestType { get; }

        /// <summary>
        /// Type of the result, when wrapped in an RpcResponse.
        /// </summary>
        public string ResponseReturnType { get; }

        /// <summary>
        /// Type of the RpcResponse for this operation. May be <see cref="RpcResponse"/>, 
        /// or <see cref="RpcResponse{T}"/> where <c>T</c>
        /// is the <see cref="ResponseReturnType"/>.
        /// </summary>
        public string ResponseType { get; }

        /// <summary>
        /// Return type as declared by service interface. If service method is async, then Task type has been unwrapped (e.g. 
        /// <c>ReturnType</c> for <see cref="Task{TResult}"/> will be typeof(TResult) ).
        /// </summary>
        public ITypeSymbol ReturnType { get; }

        ///// <summary>
        ///// Not implemented yet.
        ///// </summary>
        //public IRpcSerializer? SerializerOverride { get; }

        internal ServiceOperationReturnKind ReturnKind { get; }
    }

    public class RpcPropertyInfo : RpcMemberInfo
    {
        internal RpcPropertyInfo(
            RpcServiceInfo service,
            IPropertySymbol propertyInfo,
            ServiceOperationReturnKind propertyTypeKind,
            string responseReturnType,
            RpcOperationInfo? getOp,
            RpcOperationInfo? setOp,
            ImmutableArray<object> metadata)
            : base(propertyInfo?.Name!, service, propertyInfo!, metadata)
        {
            this.Property = propertyInfo ?? throw new ArgumentNullException(nameof(propertyInfo));
            this.PropertyTypeKind = propertyTypeKind;
            this.ResponseReturnType = responseReturnType;

            this.GetOp = getOp;
            this.SetOp = setOp;
        }

        public RpcOperationInfo? GetOp { get; }

        public RpcOperationInfo? SetOp { get; }


        public IPropertySymbol Property { get; }

        public string ResponseReturnType { get; }

        internal ServiceOperationReturnKind PropertyTypeKind { get; }
    }

    public class RpcServiceInfo
    {
        public RpcServiceInfo(string @namespace, string name, ITypeSymbol type, RpcServiceDefinitionSide definitionSide, ITypeSymbol? serverType, ITypeSymbol? implementationType, bool isSingleton,
            ImmutableArray<object> metadata)
        {
            this.Namespace = @namespace ?? throw new ArgumentNullException(nameof(@namespace));
            this.Name = name ?? throw new ArgumentNullException(nameof(name));
            this.Type = type ?? throw new ArgumentNullException(nameof(type));
            this.DefinitionSide = definitionSide;
            this.ServerType = serverType;
            this.ImplementationType = implementationType;
            this.IsSingleton = isSingleton;
            this.Metadata = metadata;
        }

        public RpcServiceDefinitionSide DefinitionSide { get; }

        public string FullName => $"{this.Namespace}.{this.Name}";

        public ITypeSymbol? ImplementationType { get; }

        /// <summary>
        /// Indicates whether this service is the service declared by the TService type argument.
        /// This service is the top-most service that implements all other services.
        /// </summary>
        public bool IsDeclaredService { get; internal set; }

        public bool IsSingleton { get; }

        public ImmutableArray<object> Metadata { get; }

        public string Name { get; }

        public string Namespace { get; }

        public ITypeSymbol? ServerType { get; }

        public ITypeSymbol Type { get; }
    }
}
