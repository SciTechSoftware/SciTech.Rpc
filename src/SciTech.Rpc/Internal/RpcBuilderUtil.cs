#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using SciTech.Rpc.Client;
using SciTech.Rpc.Logging;
using SciTech.Rpc.Serialization;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;

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
        public RpcRequestParameter(Type type, int index)
        {
            this.Type = type ?? throw new ArgumentNullException(nameof(type));
            this.Index = index;
        }

        public int Index { get; }

        public Type Type { get; }
    }

#pragma warning disable CA1815 // Override equals and operator equals on value types
    public struct RpcRequestTypeInfo
#pragma warning restore CA1815 // Override equals and operator equals on value types
    {
        public RpcRequestTypeInfo(Type type, ImmutableArray<RpcRequestParameter> parameters, int? cancellationTokenIndex)
        {
            this.Type = type ?? throw new ArgumentNullException(nameof(type));
            this.Parameters = parameters;
            this.CancellationTokenIndex = cancellationTokenIndex;
        }

        public int? CancellationTokenIndex { get; }

        public ImmutableArray<RpcRequestParameter> Parameters { get; }

        public Type Type { get; }
    }

#pragma warning disable CA1062 // Validate arguments of public methods
    public static class RpcBuilderUtil
    {

        private static ServiceContractAttribute? GetServiceContractAttribute(Type serviceType)
        {
            ServiceContractAttribute? contractAttribute = null;
#pragma warning disable CA1031 // Do not catch general exception types
            try
            {
                contractAttribute = serviceType.GetCustomAttribute<ServiceContractAttribute>();
            }
            catch (Exception e)
            {
                Logger.Warn(e, "Failed to retrive ServiceContractAttribute for '{type}'", serviceType);
            }
#pragma warning restore CA1031 // Do not catch general exception types

            return contractAttribute;
        }

        private static RpcServiceInfo GetServiceInfoFromContractAttribute(Type serviceType, ServiceContractAttribute contractAttribute)
        {
            string serviceName;
            string serviceNamespace;

            serviceName = GetServiceName(serviceType, contractAttribute);
            serviceNamespace = serviceType.Namespace ?? "";

            return new RpcServiceInfo
            (
                type: serviceType,
                @namespace: serviceNamespace,
                name: serviceName,
                definitionSide: RpcServiceDefinitionSide.Both,
                serverType: null,
                isSingleton: false,
                allowFault: false
            );
        }

        private static RpcServiceInfo GetServiceInfoFromRpcAttribute(Type serviceType, RpcServiceAttribute rpcAttribute)
        {
            string serviceName;
            string serviceNamespace;

            // Try to retrieve it from the server side definition
            if (rpcAttribute.ServerDefinitionType != null)
            {
                if (GetRpcServiceAttribute(rpcAttribute.ServerDefinitionType) is RpcServiceAttribute serverRpcAttribute)
                {
                    serviceName = GetServiceName(rpcAttribute.ServerDefinitionType, serverRpcAttribute);
                    serviceNamespace = GetServiceNamespace(rpcAttribute.ServerDefinitionType, serverRpcAttribute);
                }
                else if (GetServiceContractAttribute(rpcAttribute.ServerDefinitionType) is ServiceContractAttribute contractAttribute)
                {
                    serviceName = GetServiceName(rpcAttribute.ServerDefinitionType, contractAttribute);
                    serviceNamespace = rpcAttribute.ServerDefinitionType.Namespace ?? "";
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


            var definitionType = rpcAttribute.ServiceDefinitionSide;

            return new RpcServiceInfo
            (
                type: serviceType,
                @namespace: serviceNamespace,
                name: serviceName,
                definitionSide: definitionType,
                serverType: rpcAttribute.ServerDefinitionType,
                isSingleton: rpcAttribute.IsSingleton,
                allowFault: rpcAttribute.AllowFault
            );
        }

        private static RpcServiceInfo? GetServiceInfoFromType(Type serviceType, bool throwIfNotServiceType)
        {
            if (serviceType.IsInterface)
            {
                RpcServiceAttribute? rpcAttribute = GetRpcServiceAttribute(serviceType);

                if (rpcAttribute != null)
                {
                    return GetServiceInfoFromRpcAttribute(serviceType, rpcAttribute);
                }

                // Let's try with a ServiceContract attribute
                ServiceContractAttribute? contractAttribute = GetServiceContractAttribute(serviceType);
                if (contractAttribute != null)
                {
                    return GetServiceInfoFromContractAttribute(serviceType, contractAttribute);
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

        private static string GetServiceName(Type serviceType, RpcServiceAttribute? rpcAttribute)
        {
            var serviceName = rpcAttribute?.Name;
            if (string.IsNullOrEmpty(serviceName))
            {
                serviceName = GetDefaultServiceName(serviceType);
            }

            return serviceName!;
        }

        private static string GetServiceName(Type serviceType, ServiceContractAttribute? contractAttribute)
        {
            var serviceName = contractAttribute?.Name;
            if (string.IsNullOrEmpty(serviceName))
            {
                serviceName = GetDefaultServiceName(serviceType);
            }

            return serviceName!;
        }

        private static string GetServiceNamespace(Type serviceType, RpcServiceAttribute? rpcAttribute)
        {
            var serviceNamespace = rpcAttribute?.Namespace;
            if (string.IsNullOrEmpty(serviceNamespace))
            {
                serviceNamespace = serviceType.Namespace ?? "";
            }

            return serviceNamespace!;
        }

        private static readonly ILog Logger = LogProvider.GetLogger(typeof(RpcBuilderUtil));

        /// <summary>
        /// Enumerates all declared RPC members in the service interface specified by <paramref name="serviceInfo"/>.
        /// </summary>
        /// <param name="serviceInfo"></param>
        /// <param name="splitProperties">Indicates that separate <see cref="RpcOperationInfo"/>s should be returned for property get/set 
        /// methods, instead of a single <see cref="RpcPropertyInfo"/>.</param>
        /// <returns></returns>
        // TODO: This method should maybe be moved to RpcServiceInfo, or at least be an RpcServiceInfo extension method.
        public static IEnumerable<RpcMemberInfo> EnumOperationHandlers(RpcServiceInfo serviceInfo, bool splitProperties)
        {
            var handledMembers = new HashSet<MemberInfo>();

            var events = serviceInfo.Type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var eventInfo in events)
            {
                if (eventInfo.EventHandlerType == null)
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

            var properties = serviceInfo.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var propertyInfo in properties)
            {
                var rpcPropertyInfo = RpcBuilderUtil.GetPropertyInfoFromProperty(serviceInfo, propertyInfo);
                var propertyRpcAttribute = propertyInfo.GetCustomAttribute<RpcOperationAttribute>();

                if (propertyInfo.GetMethod != null)
                {
                    if (splitProperties)
                    {
                        var getRpcAttribute = propertyInfo.GetMethod.GetCustomAttribute<RpcOperationAttribute>();

                        bool allowFault = getRpcAttribute?.AllowFault ?? propertyRpcAttribute?.AllowFault ?? serviceInfo.AllowFault;
                        var getOp = new RpcOperationInfo(
                            service: rpcPropertyInfo.Service,
                            name: $"Get{propertyInfo.Name}",
                            declaringMember: propertyInfo,
                            method: propertyInfo.GetMethod,
                            requestType: typeof(RpcObjectRequest),
                            requestParameters: ImmutableArray<RpcRequestParameter>.Empty,
                            cancellationTokenIndex: null,
                            methodType: RpcMethodType.Unary,
                            isAsync: false,
                            responseType: GetResponseType(RpcMethodType.Unary, rpcPropertyInfo.ResponseReturnType, allowFault),
                            responseReturnType: rpcPropertyInfo.ResponseReturnType,
                            returnType: propertyInfo.PropertyType,
                            returnKind: rpcPropertyInfo.PropertyTypeKind,
                            allowInlineExecution: getRpcAttribute?.AllowInlineExecution ?? propertyRpcAttribute?.AllowInlineExecution ?? false,
                            allowFault: allowFault
                            );

                        yield return getOp;
                    }

                    handledMembers.Add(propertyInfo.GetMethod);
                }

                if (propertyInfo.SetMethod != null)
                {
                    if (splitProperties)
                    {
                        var setRpcAttribute = propertyInfo.SetMethod.GetCustomAttribute<RpcOperationAttribute>();

                        bool allowFault = setRpcAttribute?.AllowFault ?? propertyRpcAttribute?.AllowFault ?? serviceInfo.AllowFault;
                        var setOp = new RpcOperationInfo(
                            service: rpcPropertyInfo.Service,
                            name: $"Set{propertyInfo.Name}",
                            declaringMember: propertyInfo,
                            method: propertyInfo.SetMethod,
                            requestType: typeof(RpcObjectRequest<>).MakeGenericType(propertyInfo.PropertyType),
                            requestParameters: ImmutableArray.Create(
                                new RpcRequestParameter(propertyInfo.PropertyType, 0)),
                            cancellationTokenIndex: null,
                            methodType: RpcMethodType.Unary,
                            isAsync: false,
                            responseType: allowFault ? typeof(RpcResponseWithError) : typeof(RpcResponse),
                            returnType: typeof(void),
                            responseReturnType: typeof(void),
                            returnKind: ServiceOperationReturnKind.Standard,
                            allowInlineExecution: setRpcAttribute?.AllowInlineExecution ?? propertyRpcAttribute?.AllowInlineExecution ?? false,
                            allowFault: allowFault
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

            foreach (var method in serviceInfo.Type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (handledMembers.Add(method))
                {
                    var opInfo = RpcBuilderUtil.GetOperationInfoFromMethod(serviceInfo, method);
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

        public static List<RpcServiceInfo> GetAllServices<TService>(bool ignoreUnknownInterfaces)
        {
            return GetAllServices(typeof(TService), RpcServiceDefinitionSide.Both, ignoreUnknownInterfaces);
        }

        public static List<RpcServiceInfo> GetAllServices(Type serviceType, RpcServiceDefinitionSide serviceDefinitionType, bool ignoreUnknownInterfaces)
        {
            var allServices = new List<RpcServiceInfo>();
            var declaredServiceInfo = GetServiceInfoFromType(serviceType, !ignoreUnknownInterfaces);
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

            var interfaces = serviceType.GetInterfaces();

            foreach (var inheritedInterfaceType in interfaces)
            {
                if (inheritedInterfaceType.Equals(typeof(IRpcService))
                    || inheritedInterfaceType.Equals(typeof(IEquatable<IRpcService>))
                    || inheritedInterfaceType.Equals(typeof(IDisposable)))
                {
                    continue;
                }

                var interfaceServiceInfo = GetServiceInfoFromType(inheritedInterfaceType, !ignoreUnknownInterfaces);
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

        public static RpcEventInfo GetEventInfoFromEvent(RpcServiceInfo serviceInfo, EventInfo eventInfo)
        {
            var eventHandlerType = eventInfo.EventHandlerType ?? throw new NotSupportedException($"{eventInfo.Name} has no EventHandlerType"); ;
            Type eventArgsType;
            if (eventHandlerType.IsGenericType)
            {
                var eventHandlerGenericType = eventHandlerType.GetGenericTypeDefinition();
                if (eventHandlerGenericType != typeof(EventHandler<>))
                {
                    throw new RpcDefinitionException("Event handler must be EventHandler, or EventHandler<>.");
                }

                eventArgsType = eventHandlerType.GetGenericArguments()[0];
            }
            else
            {
                if (eventHandlerType != typeof(EventHandler))
                {
                    throw new RpcDefinitionException("Event handler must be EventHandler, or EventHandler<>.");
                }
                eventArgsType = typeof(EventArgs);
            }

            return new RpcEventInfo
            (
                service: serviceInfo,
                eventInfo: eventInfo,
                eventArgsType: eventArgsType
            );
        }

        public static RpcOperationInfo GetOperationInfoFromMethod(RpcServiceInfo serviceInfo, MethodInfo method)
        {
            var parameters = method.GetParameters();

            var requestTypeInfo = GetRequestType(parameters, serviceInfo.IsSingleton);

            Type actualReturnType = method.ReturnType;
            bool isAsync = false;
            RpcMethodType methodType = RpcMethodType.Unary;
            if (method.ReturnType.IsGenericType)
            {
                var genericTypeDef = method.ReturnType.GetGenericTypeDefinition();
                if (genericTypeDef.Equals(typeof(IAsyncEnumerable<>)))// || genericTypeDef.Equals(typeof(IAsyncEnumerator<>)))
                {
                    actualReturnType = method.ReturnType.GenericTypeArguments[0];
                    methodType = RpcMethodType.ServerStreaming;
                    isAsync = true;
                }
                else if (genericTypeDef.Equals(typeof(Task<>)) || genericTypeDef.Equals(typeof(ValueTask<>)))
                {
                    actualReturnType = method.ReturnType.GenericTypeArguments[0];
                    isAsync = true;
                }
            }
            else if (method.ReturnType == typeof(Task))
            {
                actualReturnType = typeof(void);
                isAsync = true;
            }
            else
            {
                actualReturnType = method.ReturnType;
            }


            string? operationName = null;

            var rpcAttribute = method.GetCustomAttribute<RpcOperationAttribute>();
            if (rpcAttribute != null)
            {
                operationName = rpcAttribute.Name;
            }

            if (string.IsNullOrEmpty(operationName))
            {
                operationName = method.Name;
                if (isAsync && operationName.EndsWith("Async", StringComparison.Ordinal))
                {
                    operationName = operationName.Substring(0, operationName.Length - "Async".Length);
                }
            }

            bool? opAllowFault = rpcAttribute?.AllowFault;
            if (opAllowFault == null)
            {
                // If AllowFault is not specified, set it to true
                // if the operation has any fault attributes.
                if (method.GetCustomAttributes<RpcFaultAttribute>().Any())
                {
                    opAllowFault = true;
                }
            }

            bool allowFault = opAllowFault ?? serviceInfo.AllowFault;

            var (returnKind, responseReturnType) = GetOperationReturnKind(actualReturnType);
            Type responseType = GetResponseType(methodType, responseReturnType, allowFault);

            return new RpcOperationInfo
            (
                service: serviceInfo,
                method: method,
                declaringMember: method,
                methodType: methodType,
                isAsync: isAsync,
                name: operationName!,
                requestParameters: requestTypeInfo.Parameters,
                cancellationTokenIndex: requestTypeInfo.CancellationTokenIndex,
                requestType: requestTypeInfo.Type,
                returnType: actualReturnType,
                responseType: responseType,
                responseReturnType: responseReturnType,
                returnKind: returnKind,
                allowInlineExecution: rpcAttribute?.AllowInlineExecution ?? false,
                allowFault: allowFault
            );
        }

        public static RpcPropertyInfo GetPropertyInfoFromProperty(RpcServiceInfo serviceInfo, PropertyInfo propertyInfo)
        {
            var propertyType = propertyInfo.PropertyType;
            var (returnKind, responseReturnType) = GetOperationReturnKind(propertyType);

            return new RpcPropertyInfo
            (
                service: serviceInfo,
                propertyInfo: propertyInfo,
                propertyTypeKind: returnKind,
                responseReturnType: responseReturnType
            );
        }

        public static RpcRequestTypeInfo GetRequestType(IReadOnlyList<ParameterInfo> parameters, bool isSingleton)
        {
            int? cancellationTokenIndex = null;
            var parametersBuilder = ImmutableArray.CreateBuilder<RpcRequestParameter>(parameters.Count);
            var parameterTypesList = new List<Type>(parameters.Count);
            for (int parameterIndex = 0; parameterIndex < parameters.Count; parameterIndex++)
            {
                var parameterInfo = parameters[parameterIndex];
                // Handle special parameter, currently only CancellationToken.
                if (typeof(CancellationToken).Equals(parameterInfo.ParameterType))
                {
                    if (cancellationTokenIndex != null)
                    {
                        throw new RpcDefinitionException("RPC operation can only include a single CancellationToken.");
                    }
                    cancellationTokenIndex = parameterIndex;
                }
                else
                {
                    parametersBuilder.Add(new RpcRequestParameter(parameterInfo.ParameterType, parameterIndex));
                    parameterTypesList.Add(parameterInfo.ParameterType);
                }
            }

            parametersBuilder.Capacity = parametersBuilder.Count;
            var requestParameters = parametersBuilder.MoveToImmutable();

            Type[] parameterTypes = parameterTypesList.ToArray();
            Type requestType;
            if (isSingleton)
            {
                switch (parameterTypes.Length)
                {
                    case 0:
                        requestType = typeof(RpcRequest);
                        break;
                    case 1:
                        requestType = typeof(RpcRequest<>).MakeGenericType(parameterTypes);
                        break;
                    case 2:
                        requestType = typeof(RpcRequest<,>).MakeGenericType(parameterTypes);
                        break;
                    case 3:
                        requestType = typeof(RpcRequest<,,>).MakeGenericType(parameterTypes);
                        break;
                    case 4:
                        requestType = typeof(RpcRequest<,,,>).MakeGenericType(parameterTypes);
                        break;
                    case 5:
                        requestType = typeof(RpcRequest<,,,,>).MakeGenericType(parameterTypes);
                        break;
                    case 6:
                        requestType = typeof(RpcRequest<,,,,,>).MakeGenericType(parameterTypes);
                        break;
                    case 7:
                        requestType = typeof(RpcRequest<,,,,,,>).MakeGenericType(parameterTypes);
                        break;
                    case 8:
                        requestType = typeof(RpcRequest<,,,,,,,>).MakeGenericType(parameterTypes);
                        break;
                    case 9:
                        requestType = typeof(RpcRequest<,,,,,,,,>).MakeGenericType(parameterTypes);
                        break;
                    default:
                        throw new NotImplementedException("An RPC operation is currently limited to 9 parameters.");
                }
            }
            else
            {
                switch (parameterTypes.Length)
                {
                    case 0:
                        requestType = typeof(RpcObjectRequest);
                        break;
                    case 1:
                        requestType = typeof(RpcObjectRequest<>).MakeGenericType(parameterTypes);
                        break;
                    case 2:
                        requestType = typeof(RpcObjectRequest<,>).MakeGenericType(parameterTypes);
                        break;
                    case 3:
                        requestType = typeof(RpcObjectRequest<,,>).MakeGenericType(parameterTypes);
                        break;
                    case 4:
                        requestType = typeof(RpcObjectRequest<,,,>).MakeGenericType(parameterTypes);
                        break;
                    case 5:
                        requestType = typeof(RpcObjectRequest<,,,,>).MakeGenericType(parameterTypes);
                        break;
                    case 6:
                        requestType = typeof(RpcObjectRequest<,,,,,>).MakeGenericType(parameterTypes);
                        break;
                    case 7:
                        requestType = typeof(RpcObjectRequest<,,,,,,>).MakeGenericType(parameterTypes);
                        break;
                    case 8:
                        requestType = typeof(RpcObjectRequest<,,,,,,,>).MakeGenericType(parameterTypes);
                        break;
                    case 9:
                        requestType = typeof(RpcObjectRequest<,,,,,,,,>).MakeGenericType(parameterTypes);
                        break;
                    default:
                        throw new NotImplementedException("An RPC operation is currently limited to 9 parameters.");
                }
            }

            return new RpcRequestTypeInfo(requestType, requestParameters, cancellationTokenIndex);

        }

        public static RpcServiceInfo GetServiceInfoFromType(Type serviceType)
        {
            return GetServiceInfoFromType(serviceType, true)!;
        }

        public static RpcServiceInfo? TryGetServiceInfoFromType(Type serviceType)
        {
            return GetServiceInfoFromType(serviceType, false);
        }

        internal static List<RpcServiceInfo> GetAllServices(Type serviceType, bool ignoreUnknownInterfaces)
        {
            return GetAllServices(serviceType, RpcServiceDefinitionSide.Both, ignoreUnknownInterfaces);
        }

        internal static (ServiceOperationReturnKind, Type) GetOperationReturnKind(Type returnType)
        {
            // TODO: Should use a [return:SerializeAs] attribute as well.
            var rpcAttribute = returnType.GetCustomAttribute<RpcServiceAttribute>();
            if (rpcAttribute != null)
            {
                var responseReturnType = typeof(RpcObjectRef);
                return (ServiceOperationReturnKind.Service, responseReturnType);
            }
            else
            {
                if (typeof(RpcObjectRef).IsAssignableFrom(returnType))
                {
                    var responseReturnType = typeof(RpcObjectRef);
                    return (ServiceOperationReturnKind.ServiceRef, responseReturnType);
                }
            }
            if (returnType.IsArray)
            {
                var elementType = returnType.GetElementType();
                if (elementType != null)
                {
                    var elementRpcAttribute = elementType.GetCustomAttribute<RpcServiceAttribute>();
                    if (elementRpcAttribute != null)
                    {
                        var responseReturnType = typeof(RpcObjectRef).MakeArrayType();
                        return (ServiceOperationReturnKind.ServiceArray, responseReturnType);
                    }
                    else
                    {
                        if (typeof(RpcObjectRef).IsAssignableFrom(elementType))
                        {
                            var responseReturnType = typeof(RpcObjectRef).MakeArrayType();
                            return (ServiceOperationReturnKind.ServiceRefArray, responseReturnType);
                        }
                    }
                }
            }

            return (ServiceOperationReturnKind.Standard, returnType);
        }

        internal static bool IsRpcServiceType(Type serviceType)
        {
            var rpcAttribute = serviceType.GetCustomAttribute<RpcServiceAttribute>();
            return rpcAttribute != null;
        }

        internal static string RetrieveFaultCode(Type faultType)
        {
            if (faultType == null)
            {
                return "";
            }

            var detailsAttribute = faultType.GetCustomAttribute<RpcFaultDetailsAttribute>();
            if (!string.IsNullOrEmpty(detailsAttribute?.FaultCode))
            {
                return detailsAttribute!.FaultCode!;
            }

            return faultType.Name;
        }

        private static string GetDefaultServiceName(Type serviceType)
        {
            string serviceName = serviceType.Name;
            if (serviceName.StartsWith("I", StringComparison.Ordinal) && serviceName.Length > 1 && char.IsUpper(serviceName[1]))
            {
                serviceName = serviceName.Substring(1);
            }

            return serviceName;
        }

        private static Type GetResponseType(RpcMethodType methodType, Type responseReturnType, bool allowFault)
        {
            if (methodType == RpcMethodType.ServerStreaming)
            {
                // Streaming errors not yet implemented.
                return responseReturnType;
            }

            if (typeof(void).Equals(responseReturnType))
            {
                return allowFault ? typeof(RpcResponseWithError) : typeof(RpcResponse);
            }

            return (allowFault ? typeof(RpcResponseWithError<>) : typeof(RpcResponse<>)).MakeGenericType(responseReturnType);
        }

        private static RpcServiceAttribute? GetRpcServiceAttribute(Type serviceType)
        {
            RpcServiceAttribute? rpcAttribute = null;
#pragma warning disable CA1031 // Do not catch general exception types
            try
            {
                rpcAttribute = serviceType.GetCustomAttribute<RpcServiceAttribute>();
            }
            catch (Exception e)
            {
                Logger.Warn(e, "Failed to retrive RpcServiceAttribute for '{type}'", serviceType);
            }
#pragma warning restore CA1031 // Do not catch general exception types

            return rpcAttribute;
        }
    }

#pragma warning restore CA1062 // Validate arguments of public methods

    public class RpcEventInfo : RpcMemberInfo
    {
        public RpcEventInfo(RpcServiceInfo service, EventInfo eventInfo, Type eventArgsType) : base(eventInfo?.Name!, service, eventInfo!)
        {
            this.Event = eventInfo ?? throw new ArgumentNullException(nameof(eventInfo));
            this.EventArgsType = eventArgsType ?? throw new ArgumentNullException(nameof(eventArgsType));
        }

        public EventInfo Event { get; }

        public Type EventArgsType { get; }
    }

    public class RpcMemberInfo
    {
        public RpcMemberInfo(string name, RpcServiceInfo service, MemberInfo declaringMember)
        {
            this.Name = name ?? throw new ArgumentNullException(nameof(name));
            this.Service = service ?? throw new ArgumentNullException(nameof(service));
            this.DeclaringMember = declaringMember ?? throw new ArgumentNullException(nameof(declaringMember));
        }

        public MemberInfo DeclaringMember { get; }

        public string FullName => $"{this.Service.FullName}.{this.Name}";

        public string FullServiceName => this.Service?.FullName ?? "";

        public string Name { get; }

        public RpcServiceInfo Service { get; }
    }

    public class RpcOperationInfo : RpcMemberInfo
    {
        internal RpcOperationInfo(string name,
            RpcServiceInfo service,
            MemberInfo declaringMember,
            MethodInfo method,
            RpcMethodType methodType,
            bool isAsync,
            ImmutableArray<RpcRequestParameter> requestParameters,
            int? cancellationTokenIndex,
            Type returnType,
            Type responseReturnType,
            Type requestType,
            Type responseType,
            ServiceOperationReturnKind returnKind,
            bool allowInlineExecution,
            bool allowFault) :
            base(name, service, declaringMember)
        {
            this.Method = method ?? throw new ArgumentNullException(nameof(method));
            this.MethodType = methodType;
            this.IsAsync = isAsync;
            this.RequestParameters = !requestParameters.IsDefault ? requestParameters : throw new ArgumentNullException(nameof(requestParameters));
            this.CancellationTokenIndex = cancellationTokenIndex;
            this.ReturnType = returnType ?? throw new ArgumentNullException(nameof(returnType));
            this.ResponseReturnType = responseReturnType ?? throw new ArgumentNullException(nameof(responseReturnType));
            this.RequestType = requestType ?? throw new ArgumentNullException(nameof(requestType));
            this.ResponseType = responseType ?? throw new ArgumentNullException(nameof(responseType));
            this.ReturnKind = returnKind;
            this.AllowInlineExecution = allowInlineExecution;
            this.AllowFault = allowFault;
        }

        public bool AllowFault { get; }

        public bool AllowInlineExecution { get; }

        public int? CancellationTokenIndex { get; }

        public bool IsAsync { get; }

        public MethodInfo Method { get; }

        public RpcMethodType MethodType { get; }

        public ImmutableArray<RpcRequestParameter> RequestParameters { get; }

        public Type RequestType { get; }

        /// <summary>
        /// Type of the result, when wrapped in an RpcResponse.
        /// </summary>
        public Type ResponseReturnType { get; }

        /// <summary>
        /// Type of the RpcResponse for this operation. May be <see cref="RpcResponseWithError"/>, <see cref="RpcResponseWithError{T}"/>, <see cref="RpcResponse"/>, 
        /// or <see cref="RpcResponse{T}"/> where <c>T</c>
        /// is the <see cref="ResponseReturnType"/>.
        /// </summary>
        public Type ResponseType { get; }

        /// <summary>
        /// Return type as declared by service interface. If service method is async, then Task type has been unwrapped (e.g. 
        /// <c>ReturnType</c> for <see cref="Task{TResult}"/> will be typeof(TResult) ).
        /// </summary>
        public Type ReturnType { get; }

        /// <summary>
        /// Not implemented yet.
        /// </summary>
        public IRpcSerializer? SerializerOverride { get; }

        internal ServiceOperationReturnKind ReturnKind { get; }
    }

    public class RpcPropertyInfo : RpcMemberInfo
    {
        internal RpcPropertyInfo(RpcServiceInfo service, PropertyInfo propertyInfo, ServiceOperationReturnKind propertyTypeKind, Type responseReturnType)
            : base(propertyInfo?.Name!, service, propertyInfo!)
        {
            this.Property = propertyInfo ?? throw new ArgumentNullException(nameof(propertyInfo));
            this.PropertyTypeKind = propertyTypeKind;
            this.ResponseReturnType = responseReturnType;
        }

        public PropertyInfo Property { get; }

        public Type ResponseReturnType { get; }

        internal ServiceOperationReturnKind PropertyTypeKind { get; }
    }

    public class RpcServiceInfo
    {
        public RpcServiceInfo(string @namespace, string name, Type type, RpcServiceDefinitionSide definitionSide, Type? serverType, bool isSingleton, bool allowFault)
        {
            this.Namespace = @namespace ?? throw new ArgumentNullException(nameof(@namespace));
            this.Name = name ?? throw new ArgumentNullException(nameof(name));
            this.Type = type ?? throw new ArgumentNullException(nameof(type));
            this.DefinitionSide = definitionSide;
            this.ServerType = serverType;
            this.IsSingleton = isSingleton;
            this.AllowFault = allowFault;
        }

        public bool AllowFault { get; }

        public RpcServiceDefinitionSide DefinitionSide { get; }

        public string FullName => $"{this.Namespace}.{this.Name}";

        /// <summary>
        /// Indicates whether this service is the service declared by the TService type argument.
        /// This service is the top-most service that implements all other services.
        /// </summary>
        public bool IsDeclaredService { get; internal set; }

        public bool IsSingleton { get; }

        public string Name { get; }

        public string Namespace { get; }

        public Type? ServerType { get; }

        public Type Type { get; }
    }
}
