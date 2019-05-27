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
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

namespace SciTech.Rpc.Internal
{
    public enum RpcMethodType
    {
        Unary,
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

#pragma warning disable CA1062 // Validate arguments of public methods
    public static class RpcBuilderUtil
    {

        public static RpcEventInfo GetEventInfoFromEvent(RpcServiceInfo serviceInfo, EventInfo eventInfo)
        {
            var eventHandlerType = eventInfo.EventHandlerType;
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

            (Type requestType, ImmutableArray<Type> ctorParameterTypes) = GetRequestType(parameters);

            Type actualReturnType = method.ReturnType;
            bool isAsync = false;
            if (method.ReturnType.IsGenericType)
            {
                var genericTypeDef = method.ReturnType.GetGenericTypeDefinition();
                if (genericTypeDef.Equals(typeof(Task<>)) || genericTypeDef.Equals(typeof(ValueTask<>)))
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

            Type responseType;
            var (returnKind, responseReturnType) = GetOperationReturnKind(actualReturnType);

            if (!responseReturnType.Equals(typeof(void)))
            {
                responseType = typeof(RpcResponse<>).MakeGenericType(responseReturnType);
            }
            else
            {
                responseType = typeof(RpcResponse);
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

            return new RpcOperationInfo
            (
                service: serviceInfo,
                method: method,
                declaringMember: method,
                methodType: RpcMethodType.Unary,
                isAsync: isAsync,
                name: operationName,
                requestTypeCtorArgTypes: ctorParameterTypes,
                parametersCount: ctorParameterTypes.Length - 1,
                requestType: requestType,
                returnType: actualReturnType,
                responseType: responseType,
                responseReturnType: responseReturnType,
                returnKind: returnKind
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

        public static (Type, ImmutableArray<Type>) GetRequestType(IReadOnlyList<ParameterInfo> parameters)
        {
            // TODO: Handle CancellationToken

            Type[] parameterTypes;
            Type requestType;
            switch (parameters.Count)
            {
                case 0:
                    parameterTypes = Array.Empty<Type>();
                    requestType = typeof(RpcObjectRequest);
                    break;
                case 1:
                    parameterTypes = new Type[] { parameters[0].ParameterType };

                    requestType = typeof(RpcObjectRequest<>).MakeGenericType(parameterTypes);
                    break;
                case 2:
                    parameterTypes = new Type[] {
                        parameters[0].ParameterType,
                        parameters[1].ParameterType
                    };

                    requestType = typeof(RpcObjectRequest<,>).MakeGenericType(parameterTypes);
                    break;
                case 3:
                    parameterTypes = new Type[] {
                        parameters[0].ParameterType,
                        parameters[1].ParameterType,
                        parameters[2].ParameterType
                    };

                    requestType = typeof(RpcObjectRequest<,,>).MakeGenericType(parameterTypes);
                    break;
                case 4:
                    parameterTypes = new Type[] {
                        parameters[0].ParameterType,
                        parameters[1].ParameterType,
                        parameters[2].ParameterType,
                        parameters[3].ParameterType};

                    requestType = typeof(RpcObjectRequest<,,,>).MakeGenericType(parameterTypes);
                    break;
                case 5:
                    parameterTypes = new Type[] {
                        parameters[0].ParameterType,
                        parameters[1].ParameterType,
                        parameters[2].ParameterType,
                        parameters[3].ParameterType,
                        parameters[4].ParameterType};

                    requestType = typeof(RpcObjectRequest<,,,,>).MakeGenericType(parameterTypes);
                    break;
                case 6:
                    parameterTypes = new Type[] {
                        parameters[0].ParameterType,
                        parameters[1].ParameterType,
                        parameters[2].ParameterType,
                        parameters[3].ParameterType,
                        parameters[4].ParameterType,
                        parameters[5].ParameterType};

                    requestType = typeof(RpcObjectRequest<,,,,,>).MakeGenericType(parameterTypes);
                    break;
                case 7:
                    parameterTypes = new Type[] {
                        parameters[0].ParameterType,
                        parameters[1].ParameterType,
                        parameters[2].ParameterType,
                        parameters[3].ParameterType,
                        parameters[4].ParameterType,
                        parameters[5].ParameterType,
                        parameters[6].ParameterType};

                    requestType = typeof(RpcObjectRequest<,,,,,,>).MakeGenericType(parameterTypes);
                    break;
                case 8:
                    parameterTypes = new Type[] {
                        parameters[0].ParameterType,
                        parameters[1].ParameterType,
                        parameters[2].ParameterType,
                        parameters[3].ParameterType,
                        parameters[4].ParameterType,
                        parameters[5].ParameterType,
                        parameters[6].ParameterType,
                        parameters[7].ParameterType};

                    requestType = typeof(RpcObjectRequest<,,,,,,,>).MakeGenericType(parameterTypes);
                    break;
                case 9:
                    parameterTypes = new Type[] {
                        parameters[0].ParameterType,
                        parameters[1].ParameterType,
                        parameters[2].ParameterType,
                        parameters[3].ParameterType,
                        parameters[4].ParameterType,
                        parameters[5].ParameterType,
                        parameters[6].ParameterType,
                        parameters[7].ParameterType,
                        parameters[8].ParameterType};

                    requestType = typeof(RpcObjectRequest<,,,,,,,,>).MakeGenericType(parameterTypes);
                    break;
                default:
                    throw new NotImplementedException();
            }

            var ctorTypes = ImmutableArray.CreateBuilder<Type>(parameterTypes.Length + 1);
            ctorTypes.Add(typeof(RpcObjectId));
            ctorTypes.AddRange(parameterTypes);
            return (requestType, ctorTypes.MoveToImmutable());

        }

        public static RpcServiceInfo? TryGetServiceInfoFromType(Type serviceType)
        {
            return GetServiceInfoFromType(serviceType, false);
        }

        public static RpcServiceInfo GetServiceInfoFromType(Type serviceType)
        {
            return GetServiceInfoFromType(serviceType, true)!;
        }

        /// <summary>
        /// Enumerates all declared RPC members in the service interface specified by <paramref name="serviceInfo"/>.
        /// </summary>
        /// <param name="serviceInfo"></param>
        /// <param name="splitProperties">Indicates that seperate <see cref="RpcOperationInfo"/>s should be returned for property get/set 
        /// methods, instead of a single <see cref="RpcEventInfo"/>.</param>
        /// <returns></returns>
        // TODO: This method should maybe be moved to RpcServiceInfo, or at least be an RpcServiceInfo extension method.
        public static IEnumerable<RpcMemberInfo> EnumOperationHandlers(RpcServiceInfo serviceInfo, bool splitProperties)
        {
            var handledMembers = new HashSet<MemberInfo>();

            var events = serviceInfo.Type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var eventInfo in events)
            {
                var rpcEventInfo = RpcBuilderUtil.GetEventInfoFromEvent(serviceInfo, eventInfo);
                // this.CheckEvent(rpcEventInfo);

                if( rpcEventInfo.Event.AddMethod != null )
                {
                    //var addOp = new RpcOperationInfo(
                    //    service: rpcEventInfo.Service,
                    //    name: $"{eventInfo.Name}",
                    //    declaringMember: eventInfo,
                    //    method: eventInfo.AddMethod,
                    //    requestType: typeof(RpcObjectRequest),
                    //    requestTypeCtorArgTypes: ImmutableArray.Create(typeof(RpcObjectId)),
                    //    methodType: RpcMethodType.EventAdd,
                    //    isAsync: false,
                    //    parametersCount: 0,
                    //    responseType: rpcEventInfo.EventArgsType,
                    //    responseReturnType: rpcEventInfo.EventArgsType,
                    //    returnType: rpcEventInfo.EventArgsType,
                    //    returnKind:  ServiceOperationReturnKind.Standard
                    //    );

                    //yield return addOp;

                    handledMembers.Add(rpcEventInfo.Event.AddMethod);
                }

                if (rpcEventInfo.Event.RemoveMethod != null)
                {
                    //var removeOp = new RpcOperationInfo(
                    //    service: rpcEventInfo.Service,
                    //    name: $"{eventInfo.Name}",
                    //    declaringMember: eventInfo,
                    //    method: eventInfo.RemoveMethod,
                    //    requestType: typeof(RpcObjectRequest),
                    //    requestTypeCtorArgTypes: ImmutableArray.Create(typeof(RpcObjectId)),
                    //    methodType: RpcMethodType.EventRemove,
                    //    isAsync: false,
                    //    parametersCount: 0,
                    //    responseType: rpcEventInfo.EventArgsType,
                    //    responseReturnType: rpcEventInfo.EventArgsType,
                    //    returnType: rpcEventInfo.EventArgsType,
                    //    returnKind: ServiceOperationReturnKind.Standard
                    //    );

                    //yield return removeOp;

                    handledMembers.Add(rpcEventInfo.Event.RemoveMethod);
                }

                yield return rpcEventInfo;
            }

            var properties = serviceInfo.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var propertyInfo in properties)
            {
                var rpcPropertyInfo = RpcBuilderUtil.GetPropertyInfoFromProperty(serviceInfo, propertyInfo);
                // this.CheckOperation(rpcPropertyInfo.FullName);

                if (propertyInfo.GetMethod != null)
                {
                    if (splitProperties)
                    {
                        var getOp = new RpcOperationInfo(
                            service: rpcPropertyInfo.Service,
                            name: $"Get{propertyInfo.Name}",
                            declaringMember: propertyInfo,
                            method: propertyInfo.GetMethod,
                            requestType: typeof(RpcObjectRequest),
                            requestTypeCtorArgTypes: ImmutableArray.Create(typeof(RpcObjectId)),
                            methodType: RpcMethodType.Unary,
                            isAsync: false,
                            parametersCount: 0,
                            responseType: typeof(RpcResponse<>).MakeGenericType(rpcPropertyInfo.ResponseReturnType),
                            responseReturnType: rpcPropertyInfo.ResponseReturnType,
                            returnType: propertyInfo.PropertyType,
                            returnKind: rpcPropertyInfo.PropertyTypeKind
                            );

                        yield return getOp;
                    }

                    handledMembers.Add(propertyInfo.GetMethod);
                }

                if ( propertyInfo.SetMethod != null)
                {
                    if (splitProperties)
                    {
                        var setOp = new RpcOperationInfo(
                            service: rpcPropertyInfo.Service,
                            name: $"Set{propertyInfo.Name}",
                            declaringMember: propertyInfo,
                            method: propertyInfo.SetMethod,
                            requestType: typeof(RpcObjectRequest<>).MakeGenericType(propertyInfo.PropertyType),
                            requestTypeCtorArgTypes: ImmutableArray.Create<Type>(typeof(RpcObjectId), propertyInfo.PropertyType),
                            methodType: RpcMethodType.Unary,
                            isAsync: false,
                            parametersCount: 1,
                            responseType: typeof(RpcResponse),
                            returnType: typeof(void),
                            responseReturnType: typeof(void),
                            returnKind: ServiceOperationReturnKind.Standard
                        );

                        yield return setOp;
                    }

                    handledMembers.Add(propertyInfo.SetMethod);
                }

                if( !splitProperties)
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
                            yield return opInfo;
                            break;

                    }
                }
            }

        }

        private static RpcServiceInfo? GetServiceInfoFromType(Type serviceType, bool throwOnError)
        {
            RpcServiceAttribute? rpcAttribute = GetRpcServiceAttribute(serviceType, throwOnError);

            if (rpcAttribute == null)
            {
                if (throwOnError)
                {
                    // The RpcService attribute is actually not strictly necessary, but I think
                    // it's good to show the intention that an interface should be used as an RPC interface.
                    throw new ArgumentException("Interface must be tagged with the RpcService attribute to allow RPC proxy/stub to be generated.");
                }

                return null;
            }

            string serviceName;
            string serviceNamespace;

            // Try to retrieve it from the server side definition
            if (rpcAttribute.ServerDefinitionType != null)
            {
                RpcServiceAttribute? serverRpcAttribute = GetRpcServiceAttribute(rpcAttribute.ServerDefinitionType, throwOnError);
                serviceName = GetServiceName(rpcAttribute.ServerDefinitionType, serverRpcAttribute);
                serviceNamespace = GetServiceNamespace(rpcAttribute.ServerDefinitionType, serverRpcAttribute);
            } else
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

            if (string.IsNullOrEmpty(rpcAttribute.Name))
            {
                // Try to retrieve it from the server side definition
                if (rpcAttribute.ServerDefinitionType != null)
                {
                    RpcServiceAttribute? serverRpcAttribute = GetRpcServiceAttribute(rpcAttribute.ServerDefinitionType, throwOnError);
                    serviceName = GetServiceName(rpcAttribute.ServerDefinitionType, serverRpcAttribute);
                }
                else
                {
                    serviceName = GetServiceName(serviceType, rpcAttribute);
                }
            }

            var definitionType = rpcAttribute.ServiceDefinitionSide;

            return new RpcServiceInfo
            (
                type: serviceType,
                @namespace: serviceNamespace,
                name: serviceName,
                definitionSide: definitionType,
                serverType: rpcAttribute.ServerDefinitionType
            );
        }

        private static string GetServiceNamespace(Type serviceType, RpcServiceAttribute? rpcAttribute)
        {
            var serviceNamespace = rpcAttribute?.Namespace;
            if (string.IsNullOrEmpty(serviceNamespace))
            {
                serviceNamespace = serviceType.Namespace;
            }

            return serviceNamespace;
        }

        private static string GetServiceName(Type serviceType, RpcServiceAttribute? rpcAttribute)
        {
            var serviceName = rpcAttribute?.Name;
            if (string.IsNullOrEmpty(serviceName))
            {
                serviceName = serviceType.Name;
                if (serviceName.StartsWith("I", StringComparison.Ordinal) && serviceName.Length > 1 && char.IsUpper(serviceName[1]))
                {
                    serviceName = serviceName.Substring(1);
                }
            }

            return serviceName;
        }

        private static RpcServiceAttribute? GetRpcServiceAttribute(Type serviceType, bool throwOnError)
        {
            RpcServiceAttribute? rpcAttribute = null;
            try
            {
                rpcAttribute = serviceType.GetCustomAttribute<RpcServiceAttribute>();
            }
            catch (Exception)
            {
                if (throwOnError)
                {
                    throw;
                }
            }

            return rpcAttribute;
        }

        public static List<RpcServiceInfo> GetAllServices<TService>(bool ignoreUnknownInterfaces)
        {
            return GetAllServices(typeof(TService), RpcServiceDefinitionSide.Both, ignoreUnknownInterfaces);
        }

        internal static List<RpcServiceInfo> GetAllServices(Type serviceType, bool ignoreUnknownInterfaces)
        {
            return GetAllServices(serviceType, RpcServiceDefinitionSide.Both, ignoreUnknownInterfaces);
        }

        internal static List<RpcServiceInfo> GetAllServices(Type serviceType, RpcServiceDefinitionSide serviceDefinitionType, bool ignoreUnknownInterfaces)
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
        // DUMMY:
        //public class SerializeAsAttribute : Attribute
        //{
        //    public SerializeAsAttribute(Type serializationType)
        //    {
        //        this.SerializationType = serializationType;
        //    }
        //    public Type SerializationType { get; }
        //}

        //[SerializeAs(typeof(RpcObjectRef))]
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
                return detailsAttribute.FaultCode;
            }

            return faultType.Name;
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

        public string FullName => $"{this.Service.FullName}.{this.Name}";

        public string FullServiceName => this.Service?.FullName ?? "";

        public string Name { get; }

        public MemberInfo DeclaringMember { get; }

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
            ImmutableArray<Type> requestTypeCtorArgTypes,
            int parametersCount,
            Type returnType,
            Type responseReturnType,
            Type requestType,
            Type responseType,
            ServiceOperationReturnKind returnKind) :
            base(name, service, declaringMember)
        {
            this.Method = method ?? throw new ArgumentNullException(nameof(method));
            this.MethodType = methodType;
            this.IsAsync = isAsync;
            this.RequestTypeCtorArgTypes = !requestTypeCtorArgTypes.IsDefault ? requestTypeCtorArgTypes : throw new ArgumentNullException(nameof(requestTypeCtorArgTypes));
            this.ParametersCount = parametersCount;
            this.ReturnType = returnType ?? throw new ArgumentNullException(nameof(returnType));
            this.ResponseReturnType = responseReturnType ?? throw new ArgumentNullException(nameof(responseReturnType));
            this.RequestType = requestType ?? throw new ArgumentNullException(nameof(requestType));
            this.ResponseType = responseType ?? throw new ArgumentNullException(nameof(responseType));
            this.ReturnKind = returnKind;
        }

        public bool IsAsync { get; }


        public MethodInfo Method { get; }

        public RpcMethodType MethodType { get; }

        public int ParametersCount { get; }

        public Type RequestType { get; }

        public ImmutableArray<Type> RequestTypeCtorArgTypes { get; }

        /// <summary>
        /// Type of the result, when wrapped in an RpcResponse.
        /// </summary>
        public Type ResponseReturnType { get; }

        /// <summary>
        /// Type of the RpcResponse for this operation. May be <see cref="RpcResponse"/> or <see cref="RpcResponse{T}"/> where <c>T</c>
        /// is the <see cref="ResponseReturnType"/>.
        /// </summary>
        public Type ResponseType { get; }

        internal ServiceOperationReturnKind ReturnKind { get; }

        /// <summary>
        /// Return type as declared by service interface. If service method is async, then Task type has been unwrapped (e.g. 
        /// <c>ReturnType</c> for <see cref="Task{TResult}"/> will be typeof(TResult) ).
        /// </summary>
        public Type ReturnType { get; }

        public IRpcSerializer? Serializer { get; }
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

        internal ServiceOperationReturnKind PropertyTypeKind { get; }

        public Type ResponseReturnType { get; }
    }

    public class RpcServiceInfo
    {
        public RpcServiceInfo(string @namespace, string name, Type type, RpcServiceDefinitionSide definitionSide, Type? serverType )
        {
            this.Namespace = @namespace ?? throw new ArgumentNullException(nameof(@namespace));
            this.Name = name ?? throw new ArgumentNullException(nameof(name));
            this.Type = type ?? throw new ArgumentNullException(nameof(type));
            this.DefinitionSide = definitionSide;
            this.ServerType = serverType;
        }

        public RpcServiceDefinitionSide DefinitionSide { get; }

        public string FullName => $"{this.Namespace}.{this.Name}";

        /// <summary>
        /// Indicates whether this service is the service declared by the TService type argument.
        /// This service is the top-most service that implemenents all other services.
        /// </summary>
        public bool IsDeclaredService { get; internal set; }

        public string Name { get; }

        public string Namespace { get; }

        public Type Type { get; }

        public Type? ServerType { get; }
    }
}
