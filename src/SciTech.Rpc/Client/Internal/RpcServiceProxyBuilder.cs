#region Copyright notice and license

// Copyright (c) 2019-2021, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License.
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//

#endregion Copyright notice and license

using SciTech.Rpc.Internal;
using SciTech.Rpc.Serialization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

namespace SciTech.Rpc.Client.Internal
{
    /// <summary>
    /// Helper class for <see cref="RpcProxyGenerator{TRpcProxy, TProxyArgs, TMethodDef}"/> which is used to create the actual proxy factories.
    /// </summary>
    internal class RpcServiceProxyBuilder<TRpcProxyBase, TMethodDef>
        where TRpcProxyBase : RpcProxyBase<TMethodDef>
        where TMethodDef : RpcProxyMethod
    {
        private readonly Dictionary<string, int> definedProxyTypes;

        private readonly Dictionary<string, MethodDefIndex> eventDataFields = new Dictionary<string, MethodDefIndex>();

        private readonly Dictionary<string, MethodDefIndex> methodDefinitionIndices = new Dictionary<string, MethodDefIndex>();
        ///// <summary>
        ///// Only initialized during call to BuildObjectProxyType
        ///// </summary>
        //private ParameterExpression serializerExpression;

        private readonly ModuleBuilder moduleBuilder;

        private IReadOnlyList<RpcServiceInfo> allServices;

        /// <summary>
        /// Only initialized during call to BuildObjectProxyType.
        /// </summary>
        private List<TMethodDef>? createMethodDefExpressions;

        /// <summary>
        /// Only initialized during call to BuildObjectProxyType
        /// </summary>
        private ILGenerator? staticCtorILGenerator;

        /// <summary>
        /// Only initialized during call to BuildObjectProxyType
        /// </summary>
        private TypeBuilder? typeBuilder;

        internal RpcServiceProxyBuilder(
            IReadOnlyList<RpcServiceInfo> allServices,
            ModuleBuilder moduleBuilder, Dictionary<string, int> definedProxyTypes)
        {
            this.allServices = allServices;
            this.moduleBuilder = moduleBuilder;
            this.definedProxyTypes = definedProxyTypes;
        }

        /// <summary>
        ///
        /// </summary>
        /// <typeparam name="TProxyArgs"></typeparam>
        /// <returns></returns>
        public (Func<TProxyArgs, TMethodDef[], RpcProxyBase>, TMethodDef[]) BuildObjectProxyFactory<TProxyArgs>() where TProxyArgs : RpcProxyArgs
        {
            var (proxyType, createMethodsFunc) = this.BuildObjectProxyType(new Type[] { typeof(TProxyArgs), typeof(TMethodDef[]) });

            return (
                CreateObjectProxyFactory<TProxyArgs>(proxyType),
                createMethodsFunc);
        }

        /// <summary>
        /// Creates a factory delegate from the provided <paramref name="proxyType"/>.
        /// Note that this is really a private implementation method which is made internal to allow testing. <see cref="BuildObjectProxyFactory"/>
        /// should be used by other code.
        /// </summary>
        /// <param name="proxyType">Proxy type created by <see cref="BuildObjectProxyType"/>.</param>
        /// <returns></returns>
        internal static Func<TProxyArgs, TMethodDef[], RpcProxyBase> CreateObjectProxyFactory<TProxyArgs>(Type proxyType) where TProxyArgs : RpcProxyArgs
        {
            var proxyCtor = proxyType.GetConstructor(new Type[] { typeof(TProxyArgs), typeof(TMethodDef[]) })
                ?? throw new NotImplementedException($"Constructor not found on prox type '{proxyType.Name}'.");

            var proxyArgsParameter = Expression.Parameter(typeof(TProxyArgs), "proxyArgs");
            var methodsParameter = Expression.Parameter(typeof(TMethodDef[]), "proxyMethods");
            var proxyCreatorLambda = Expression.Lambda(
                Expression.New(proxyCtor,
                    proxyArgsParameter,
                    methodsParameter),
                proxyArgsParameter,
                methodsParameter);

            return (Func<TProxyArgs, TMethodDef[], RpcProxyBase>)proxyCreatorLambda.Compile();
        }

        /// <summary>
        /// Builds a proxy type that implements all members in all service interfaces. The returned type
        /// has a constructor matching
        /// <see cref="RpcProxyBase{TMethodDef}.RpcProxyBase(RpcProxyArgs,TMethodDef[])"/>.
        /// Note that this is really a private implementation method which is made internal to allow testing. <see cref="BuildObjectProxyFactory"/>
        /// should be used by other code.
        /// </summary>
        /// <remarks>
        /// The proxy currently uses an array of method definitions to dispatch RPC calls. Each proxy method has an associated index into
        /// the method definitions array (sort of a v-table). Earlier implementation stored the method definitions as fields in the proxy
        /// type and therefore method definitions are created through a creation Expressions instead of being created directly. Storing
        /// method definitions as (static) fields may still make sense, especially in AOT compilation scenarios, so let's keep
        /// the Expression based creation for now.
        /// </remarks>
        /// <returns>A tuple containing the generated proxy type, and a factory function that can be used to
        /// created the method definitions array used by the proxy.</returns>
        internal (Type, TMethodDef[]) BuildObjectProxyType(Type[] proxyCtorArgs)
        {
            try
            {
                var declaredServiceInfo = this.allServices.Single(s => s.IsDeclaredService);

                (this.typeBuilder, this.staticCtorILGenerator) = this.CreateTypeBuilder(declaredServiceInfo, proxyCtorArgs);
                this.createMethodDefExpressions = new List<TMethodDef>();
                //this.serializerExpression = Expression.Parameter(typeof(IRpcSerializer), "serializer");

                foreach (var serviceInfo in this.allServices)
                {
                    this.typeBuilder.AddInterfaceImplementation(serviceInfo.Type);
                    this.AddServiceProxyMembers(serviceInfo);
                }

                this.EndStaticCtorILGenerator();
                var proxyType = this.typeBuilder.CreateTypeInfo();
                if (proxyType == null) throw new NotSupportedException("Failed to create proxy type.");

                return (proxyType, createMethodDefExpressions.ToArray());
            }
            finally
            {
                this.typeBuilder = null;
                this.createMethodDefExpressions = null;
                this.methodDefinitionIndices.Clear();
                this.eventDataFields.Clear();
            }
        }

        private static IRpcClientExceptionConverter CreateClientExceptionConverter(RpcFaultConverterAttribute faultAttribute)
        {
            if (faultAttribute.ConverterType != null)
            {
                var converterCtor = faultAttribute.ConverterType.GetConstructor(Type.EmptyTypes)
                    ?? throw new NotImplementedException($"{faultAttribute.ConverterType} constructor not found.");

                var converter = converterCtor.Invoke(null) as IRpcClientExceptionConverter
                    ?? throw new NotImplementedException($"{faultAttribute.ConverterType} must implement IRpcClientExceptionConverter.");
                return converter;
            }
            else if (!string.IsNullOrWhiteSpace(faultAttribute.FaultCode) && faultAttribute.ExceptionType != null)
            {
                var faultConverterType = typeof(RpcExceptionConverter<>).MakeGenericType(faultAttribute.ExceptionType);
                var converterCtor = faultConverterType.GetConstructor(new Type[] { typeof(string), typeof(bool) })
                    ?? throw new NotImplementedException($"{faultConverterType.Name} constructor not found.");

                var converter = (IRpcClientExceptionConverter)converterCtor.Invoke(new object[] { faultAttribute.FaultCode!, faultAttribute.IncludeSubTypes });
                return converter;
            }

            throw new RpcDefinitionException("Converter or FaultCode with ExceptionType must be specified in RpcFaultConverterAttribute.");
        }

        private static IRpcFaultExceptionFactory CreateFaultExceptionFactory(RpcFaultAttribute faultAttribute)
        {
            if (!string.IsNullOrEmpty(faultAttribute.FaultCode))
            {
                return CreateFaultExceptionFactory(faultAttribute.FaultCode, faultAttribute.FaultType);
            }
            else
            {
                throw new RpcDefinitionException("FaultCode must be specified in RpcFaultAttribute.");
            }
        }

        private static IRpcFaultExceptionFactory CreateFaultExceptionFactory(string faultCode, Type? faultType)
        {
            ConstructorInfo converterCtor;
            if (faultType != null)
            {
                var faultConverterType = typeof(RpcFaultExceptionFactory<>).MakeGenericType(faultType);
                converterCtor = faultConverterType.GetConstructor(new Type[] { typeof(string) })
                    ?? throw new NotImplementedException($"{faultConverterType.Name} constructor not found.");
            }
            else
            {
                var faultConverterType = typeof(RpcFaultExceptionFactory);
                converterCtor = faultConverterType.GetConstructor(new Type[] { typeof(string) })
                    ?? throw new NotImplementedException($"{nameof(RpcFaultExceptionFactory)} constructor not found.");
            }

            var converter = (IRpcFaultExceptionFactory)converterCtor.Invoke(new object[] { faultCode });
            return converter;
        }

        private static RpcClientFaultHandler CreateFaultHandlerFromAttributes(RpcClientFaultHandler? baseHandler, IEnumerable<Attribute> attributes)
        {
            var factories = new List<IRpcFaultExceptionFactory>();
            var converters = new List<IRpcClientExceptionConverter>();

            foreach (var attribute in attributes)
            {
                if (attribute is RpcFaultAttribute faultAttribute)
                {
                    factories.Add(CreateFaultExceptionFactory(faultAttribute));
                }
                else if (attribute is RpcFaultConverterAttribute converterAttribute)
                {
                    converters.Add(CreateClientExceptionConverter(converterAttribute));

                    factories.Add(CreateFaultExceptionFactory(converterAttribute.FaultCode!, null)); // converterAttribute.FaultType)); ;
                }
            }

            if (factories.Count > 0 || converters.Count > 0)
            {
                return new RpcClientFaultHandler(baseHandler, factories, converters);
            }

            return baseHandler ?? RpcClientFaultHandler.Empty;
        }

        private static TMethodDef CreateMethodDefExpression(
            RpcMemberInfo memberInfo,
            string methodName,
            RpcMethodType methodType,
            Type requestType,
            Type responseType,
            IRpcSerializer? serializer,
            RpcClientFaultHandler faultHandler)
        {
            var createMethodDefMethodDef = GetProxyMethod(RpcProxyBase<TMethodDef>.CreateMethodDefName, BindingFlags.Static | BindingFlags.Public);
            var createMethodDefMethod = createMethodDefMethodDef.MakeGenericMethod(requestType, responseType);
            return (TMethodDef?)createMethodDefMethod.Invoke(null, new object?[] { methodType, memberInfo.Service.FullName, methodName, serializer, faultHandler })
                ?? throw new InvalidOperationException("Incorrect CreateMethodDef implementatin");
        }

        private static void EmitResponseConverter(ILGenerator il, RpcOperationInfo operationInfo)
        {
            var converterField = GetResponseConverterField(operationInfo);
            if (converterField != null)
            {
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldfld, converterField);
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }
        }

        ///// <summary>
        /////
        ///// </summary>
        ///// <param name="faultAttribute"></param>
        ///// <returns></returns>
        //private static IRpcClientExceptionConverter CreateClientExceptionConverter(RpcFaultConverterAttribute converterAttribute)
        //{
        //    if (!string.IsNullOrWhiteSpace(converterAttribute.FaultCode))
        //    {
        //        ConstructorInfo converterCtor;
        //        if (converterAttribute.FaultType != null)
        //        {
        //            var faultConverterType = typeof(RpcFaultExceptionConverter<>).MakeGenericType(converterAttribute.FaultType);
        //            converterCtor = faultConverterType.GetConstructor(new Type[] { typeof(string) })
        //                ?? throw new NotImplementedException($"{faultConverterType.Name} constructor not found.");
        //        }
        //        else
        //        {
        //            var faultConverterType = typeof(RpcFaultExceptionConverter);
        //            converterCtor = faultConverterType.GetConstructor(new Type[] { typeof(string) })
        //                ?? throw new NotImplementedException($"{nameof(RpcFaultExceptionConverter)} constructor not found.");
        //        }

        //        var converter = (IRpcClientExceptionConverter)converterCtor.Invoke(new object[] { converterAttribute.FaultCode! });
        //        return converter;
        //    }
        private static FieldInfo GetProxyField(string name, BindingFlags bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance)
        {
            return typeof(TRpcProxyBase).GetField(name, bindingFlags)
                ?? throw new NotImplementedException($"Field {name} not found on type '{typeof(TRpcProxyBase)}'.");
        }

        private static MethodInfo GetProxyMethod(string name, BindingFlags bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance)
        {
            return typeof(TRpcProxyBase).GetMethod(name, bindingFlags)
                ?? throw new NotImplementedException($"Method {name} not found on type '{typeof(TRpcProxyBase)}'.");
        }

        private static FieldInfo? GetResponseConverterField(RpcOperationInfo operationInfo)
        {
            if (!Equals(operationInfo.ResponseReturnType, operationInfo.ReturnType))
            {
                FieldInfo? converterField = null;

                switch (operationInfo.ReturnKind)
                {
                    case ServiceOperationReturnKind.Service:
                        converterField = typeof(ServiceConverter<>).MakeGenericType(operationInfo.ReturnType)
                            .GetField(nameof(ServiceConverter<object>.Default), BindingFlags.Public | BindingFlags.Static);
                        break;

                    case ServiceOperationReturnKind.ServiceArray:
                        {
                            var elementType = operationInfo.ReturnType.GetElementType()
                                ?? throw new InvalidOperationException("Expected an element type.");
                            converterField = typeof(ServiceConverter<>).MakeGenericType(elementType)
                                .GetField(nameof(ServiceConverter<object>.DefaultArray), BindingFlags.Public | BindingFlags.Static);
                        }
                        break;

                    case ServiceOperationReturnKind.ServiceRef:
                        converterField = typeof(ServiceRefConverter<>).MakeGenericType(operationInfo.ReturnType.GenericTypeArguments[0])
                            .GetField(nameof(ServiceRefConverter<object>.Default), BindingFlags.Public | BindingFlags.Static);
                        break;

                    case ServiceOperationReturnKind.ServiceRefArray:
                        {
                            var elementType = operationInfo.ReturnType.GetElementType()?.GenericTypeArguments[0]
                                ?? throw new InvalidOperationException("Expected a generic element type."); ;
                            converterField = typeof(ServiceRefConverter<>).MakeGenericType(elementType)
                                .GetField(nameof(ServiceRefConverter<object>.DefaultArray), BindingFlags.Public | BindingFlags.Static);
                        }
                        break;

                    default:
                        throw new InvalidOperationException("Unknown response converter.");
                }

                return converterField;// Expression.Field(null, converterField);
            }

            return null;// NullConverterExpression;
        }

        private static IEnumerable<TAttribute> RetrieveAttributes<TAttribute>(MemberInfo clientMemberInfo, MemberInfo? serverMemberInfo, Func<TAttribute,bool>? filter=null)//, Func<TAttribute,TAttribute,bool> equalityComparer)
            where TAttribute : Attribute
        {
            // TODO: This is almost the same code as RetrieveServiceFaultAttributes, try to combine.
            List<TAttribute> faultAttributes;

            if (serverMemberInfo != null)
            {
                // If the server side definition is available, then the fault attributes of that definition
                // should be used.
                faultAttributes = serverMemberInfo.GetCustomAttributes<TAttribute>().ToList();

                // Validate that any fault attributes applied to the client side definition exists on the server side
                // definition
                foreach (var clientFaultAttribute in clientMemberInfo.GetCustomAttributes<TAttribute>())
                {
                    if (filter == null || filter(clientFaultAttribute))
                    {
                        if ( faultAttributes.Find(sa => sa.Match(clientFaultAttribute)/*equalityComparer(sa, clientFaultAttribute)*/) == null)
                        {
                            throw new RpcDefinitionException($"Client side operation definition includes attribute '{clientFaultAttribute}' which is not applied on server side definition.");
                        }
                    }
                }
            }
            else
            {
                if (filter != null)
                {
                    faultAttributes = clientMemberInfo.GetCustomAttributes<TAttribute>().Where(filter).ToList();
                }
                else
                {
                    faultAttributes = clientMemberInfo.GetCustomAttributes<TAttribute>().ToList();
                }
            }

            return faultAttributes;
        }

        private static RpcClientFaultHandler RetrieveClientFaultHandler(RpcOperationInfo operationInfo,
            RpcClientFaultHandler serviceFaultHandler,
            RpcMemberInfo? serverSideMemberInfo)
        {
            IEnumerable<Attribute> faultAttributes = RetrieveFaultAttributes(operationInfo, serverSideMemberInfo);

            return CreateFaultHandlerFromAttributes(serviceFaultHandler, faultAttributes);
        }

        private static IEnumerable<RpcFaultConverterAttribute> RetrieveConverterAttributes(RpcOperationInfo operationInfo, RpcMemberInfo? serverSideMemberInfo)
            => RetrieveAttributes<RpcFaultConverterAttribute>(operationInfo.DeclaringMember, serverSideMemberInfo?.DeclaringMember);

        //, (a1, a2) => Equals(a1.ConverterType == a2.ConverterType ) && Equals(a1.Match, a2.FaultType));
        private static List<IRpcClientExceptionConverter> RetrieveExceptionConvertersFromConverterAttributes(IEnumerable<RpcFaultConverterAttribute> converterAttributes)
        {
            var faultExceptionExpressions = new List<IRpcClientExceptionConverter>();
            foreach (RpcFaultConverterAttribute converterAttribute in converterAttributes)
            {
                var exceptionConverter = CreateClientExceptionConverter(converterAttribute);
                faultExceptionExpressions.Add(exceptionConverter);
            }

            return faultExceptionExpressions;
        }

        private static IEnumerable<Attribute> RetrieveFaultAttributes(RpcOperationInfo operationInfo, RpcMemberInfo? serverSideMemberInfo)
                                            => RetrieveAttributes<Attribute>(operationInfo.DeclaringMember, serverSideMemberInfo?.DeclaringMember, a=>a is RpcFaultAttribute || a is RpcFaultConverterAttribute);//, (a1, a2) => a1.FaultCode == a2.FaultCode && Equals(a1.FaultType, a2.FaultType));

        private static IEnumerable<RpcFaultConverterAttribute> RetrieveServiceExeptionConverterAttributes(RpcServiceInfo serviceInfo)
            => RetrieveAttributes<RpcFaultConverterAttribute>(serviceInfo.Type, serviceInfo.ServerType);

        private static IEnumerable<Attribute> RetrieveServiceFaultAttributes(RpcServiceInfo serviceInfo)
                    => RetrieveAttributes<Attribute>(serviceInfo.Type, serviceInfo.ServerType, a => a is RpcFaultAttribute || a is RpcFaultConverterAttribute);

        private int AddMethodDef(
            RpcOperationInfo memberInfo,
            RpcMethodType methodType,
            IRpcSerializer? serializer,
            RpcClientFaultHandler faultHandler)
        {
            if (this.createMethodDefExpressions == null)
            {
                throw new InvalidOperationException();
            }

            int methodDefIndex = this.createMethodDefExpressions.Count;

            this.createMethodDefExpressions.Add(
                CreateMethodDefExpression(memberInfo, memberInfo.Name, methodType, memberInfo.RequestType, memberInfo.ResponseType,
                serializer, faultHandler));
            return methodDefIndex;
        }

        private int AddMethodDef(
            RpcMemberInfo memberInfo,
            string methodName,
            RpcMethodType methodType,
            Type requestType,
            Type responseType,
            IRpcSerializer? serializer,
            RpcClientFaultHandler faultHandler)
        {
            if (this.createMethodDefExpressions == null)
            {
                throw new InvalidOperationException();
            }

            int methodDefIndex = this.createMethodDefExpressions.Count;

            this.createMethodDefExpressions.Add(
                CreateMethodDefExpression(memberInfo, methodName, methodType, requestType, responseType,
                serializer, faultHandler));
            return methodDefIndex;
        }

        /// <summary>
        /// Adds all declared members of the service interface specified by <paramref name="serviceInfo"/> to the proxy type
        /// being built.
        /// </summary>
        private void AddServiceProxyMembers(RpcServiceInfo serviceInfo)
        {
            IEnumerable<Attribute> faultAttributes = RetrieveServiceFaultAttributes(serviceInfo);

            RpcClientFaultHandler serviceFaultHandler = CreateFaultHandlerFromAttributes(null, faultAttributes);

            List<RpcMemberInfo>? serverSideMembers = null;
            if (serviceInfo.ServerType != null)
            {
                var serverServiceInfo = RpcBuilderUtil.TryGetServiceInfoFromType(serviceInfo.ServerType);
                if (serverServiceInfo == null)
                {
                    throw new RpcDefinitionException($"Server side type '{serviceInfo.ServerType}' is not an RpcService.");
                }

                serverSideMembers = RpcBuilderUtil.EnumOperationHandlers(serverServiceInfo, true).ToList();
            }

            foreach (var memberInfo in RpcBuilderUtil.EnumOperationHandlers(serviceInfo, true))
            {
                RpcMemberInfo? serverSideMemberInfo = null;
                if (serverSideMembers != null)
                {
                    serverSideMemberInfo = serverSideMembers.Find(sm => memberInfo.FullName == sm.FullName);

                    if (serverSideMemberInfo == null)
                    {
                        throw new RpcDefinitionException($"A server side operation matching '{memberInfo.FullName}' does not exist on '{serviceInfo.ServerType}'.");
                    }
                    else
                    {
                        // TODO: Validate that request and response types are compatible.
                    }
                }

                if (memberInfo is RpcEventInfo rpcEventInfo)
                {
                    this.CreateEventImpl(rpcEventInfo);
                }
                //else if (memberInfo is RpcPropertyInfo rpcPropertyInfo)
                //{
                //    this.CreatePropertyImpl(rpcPropertyInfo, serviceFaultGeneratorExpressions, serverSideMemberInfo );
                //}
                else if (memberInfo is RpcOperationInfo rpcMethodInfo)
                {
                    switch (rpcMethodInfo.MethodType)
                    {
                        case RpcMethodType.Unary:
                            if (rpcMethodInfo.IsAsync)
                            {
                                this.CreateAsyncMethodImpl(rpcMethodInfo, serviceFaultHandler, serverSideMemberInfo);
                            }
                            else
                            {
                                this.CreateBlockingMethodImpl(rpcMethodInfo, serviceFaultHandler, serverSideMemberInfo);
                            }
                            break;

                        case RpcMethodType.ServerStreaming:
                            this.CreateServerStreamingMethodImpl(rpcMethodInfo, serviceFaultHandler, serverSideMemberInfo);
                            break;
                        //case RpcMethodType.EventAdd:
                        //    CreateEventAddHandler(rpcMethodInfo);
                        //    break;
                        default:
                            throw new NotImplementedException();
                    }
                }
                else
                {
                    throw new NotImplementedException();
                }
            }
        }

        /// <summary>
        /// Creates a
        /// </summary>
        /// <remarks>
        /// Assuming the service interface:
        /// <code><![CDATA[
        /// [RpcService]
        /// namespace TestSrvices
        /// {
        ///     public interface ISimpleService
        ///     {
        ///         Task<int> AddAsync(int a, int b);
        ///     }
        /// }
        /// ]]></code>
        /// This method will generate the method for the AddAsync operation (explicitly implemented):
        /// <code><![CDATA[
        /// Task<int> ISimpleService.AddAsync(int a, int b)
        /// {
        ///     TMethodDef methodDef = this.proxyMethods[<Index_IBlockingService_Add>];
        ///     return CallUnaryMethodAsync<RpcObjectRequest<int, int>, int>(
        ///         methodDef,
        ///         new RpcObjectRequest<int, int>( this.objectId, a, b ),
        ///         "TestServices.SimpleService", "Add");
        /// }
        /// ]]></code>
        /// </remarks>
        /// <returns></returns>
        private void CreateAsyncMethodImpl(
            RpcOperationInfo operationInfo,
            RpcClientFaultHandler serviceFaultHandler,
            RpcMemberInfo? serverSideMethodInfo)
        {
            if (this.typeBuilder == null)
            {
                throw new InvalidOperationException();
            }

            var implMethodBuilder = this.typeBuilder.DefineMethod(
                $"{operationInfo.Service.Name}.{operationInfo.Method.Name}",
                MethodAttributes.Private | MethodAttributes.Virtual,
                returnType: operationInfo.Method.ReturnType,
                parameterTypes: operationInfo.Method.GetParameters().Select(p => p.ParameterType).ToArray());

            this.typeBuilder.DefineMethodOverride(implMethodBuilder, operationInfo.Method);

            var il = implMethodBuilder.GetILGenerator();

            var objectIdField = GetProxyField(RpcProxyBase<TMethodDef>.ObjectIdFieldName);
            var proxyMethodsField = GetProxyField(RpcProxyBase<TMethodDef>.ProxyMethodsFieldName);
            il.Emit(OpCodes.Ldarg_0);// Load this

            int methodDefIndex = this.CreateMethodDefinitionField(operationInfo, serviceFaultHandler, serverSideMethodInfo);

            il.Emit(OpCodes.Ldarg_0);// Load this (for proxyMethods field )
            il.Emit(OpCodes.Ldfld, proxyMethodsField); //Load method def field
            il.Emit(OpCodes.Ldc_I4, methodDefIndex);
            il.Emit(OpCodes.Ldelem, typeof(TMethodDef)); // load method def (this.proxyMethods[methodDefIndex])

            bool isSingleton = operationInfo.Service.IsSingleton;

            Type[] reqestTypeCtorArgs = new Type[operationInfo.RequestParameters.Length + (isSingleton ? 0 : 1)];
            int argIndex = 0;

            if (!isSingleton)
            {
                il.Emit(OpCodes.Ldarg_0);// Load this (for objectId field)
                il.Emit(OpCodes.Ldfld, objectIdField); //Load objectId field
                reqestTypeCtorArgs[argIndex++] = typeof(RpcObjectId);
            }

            // Load parameters
            foreach (var requestParameter in operationInfo.RequestParameters)
            {
                RpcIlHelper.EmitLdArg(il, requestParameter.Index + 1);
                reqestTypeCtorArgs[argIndex++] = requestParameter.Type;
            }

            var ctorInfo = operationInfo.RequestType.GetConstructor(reqestTypeCtorArgs)
                ?? throw new NotImplementedException($"Request type constructor not found");
            il.Emit(OpCodes.Newobj, ctorInfo);  // new RpcRequestType<>( objectId, ...)

            MethodInfo callUnaryMethodInfo;

            if (operationInfo.ReturnType != typeof(void))
            {
                string callerMethodName = RpcProxyBase<TMethodDef>.CallUnaryMethodAsyncName;

                var callUnaryMethodDefInfo = GetProxyMethod(callerMethodName);
                callUnaryMethodInfo = callUnaryMethodDefInfo.MakeGenericMethod(operationInfo.RequestType, operationInfo.ResponseReturnType, operationInfo.ReturnType);

                EmitResponseConverter(il, operationInfo);
            }
            else
            {
                string callerMethodName = RpcProxyBase<TMethodDef>.CallUnaryVoidMethodAsyncName;

                var callUnaryMethodDefInfo = GetProxyMethod(callerMethodName);
                callUnaryMethodInfo = callUnaryMethodDefInfo.MakeGenericMethod(operationInfo.RequestType);
            }

            if (operationInfo.CancellationTokenIndex != null)
            {
                RpcIlHelper.EmitLdArg(il, operationInfo.CancellationTokenIndex.Value + 1);
            }
            else
            {
                // Load CancellationToken.None
                var ctLocal = il.DeclareLocal(typeof(CancellationToken));
                il.Emit(OpCodes.Ldloca_S, ctLocal);
                il.Emit(OpCodes.Initobj, typeof(CancellationToken));
                Debug.Assert(ctLocal.LocalIndex == 0);
                il.Emit(OpCodes.Ldloc_0);//, ctLocal.LocalIndex);
            }

            il.Emit(OpCodes.Call, callUnaryMethodInfo);
            il.Emit(OpCodes.Ret); //return
        }

        /// <summary>
        /// Creates a
        /// </summary>
        /// <remarks>
        /// Assuming the service interface:
        /// <code><![CDATA[
        /// [RpcService]
        /// namespace TestServices
        /// {
        ///     public interface IBlockingService
        ///     {
        ///         int Add(int a, int b);
        ///     }
        /// }
        /// ]]></code>
        /// This method will generate the method for the Add operation (explicitly implemented):
        /// <code><![CDATA[
        /// int IBlockingService.Add(int a, int b)
        /// {
        ///     TMethodDef methodDef = this.proxyMethods[<Index_IBlockingService_Add>];
        ///
        ///     return CallUnaryMethod<RpcObjectRequest<int, int>>(
        ///         methodDef,
        ///         new RpcObjectRequest<int, int>( this.objectId, a, b ),
        ///         null,   // response converter
        ///         default // cancellation token
        ///         );
        /// }
        /// ]]></code>
        /// </remarks>
        /// <returns></returns>
        private void CreateBlockingMethodImpl(RpcOperationInfo operationInfo, RpcClientFaultHandler serviceFaultHandler, RpcMemberInfo? serverSideMemberInfo)
        {
            if (this.typeBuilder == null)
            {
                throw new InvalidOperationException();
            }

            var implMethodBuilder = this.typeBuilder.DefineMethod($"{operationInfo.Service.Name}.{operationInfo.Method.Name}", MethodAttributes.Private | MethodAttributes.Virtual,
                returnType: operationInfo.Method.ReturnType,
                parameterTypes: operationInfo.Method.GetParameters().Select(p => p.ParameterType).ToArray());

            this.typeBuilder.DefineMethodOverride(implMethodBuilder, operationInfo.Method);

            var il = implMethodBuilder.GetILGenerator();

            var objectIdField = GetProxyField(RpcProxyBase<TMethodDef>.ObjectIdFieldName);
            var proxyMethodsField = GetProxyField(RpcProxyBase<TMethodDef>.ProxyMethodsFieldName);
            int methodDefIndex = this.CreateMethodDefinitionField(operationInfo, serviceFaultHandler, serverSideMemberInfo);

            il.Emit(OpCodes.Ldarg_0);// Load this
            il.Emit(OpCodes.Ldarg_0);// Load this (for proxyMethods field )
            il.Emit(OpCodes.Ldfld, proxyMethodsField); //Load method def field
            il.Emit(OpCodes.Ldc_I4, methodDefIndex);
            il.Emit(OpCodes.Ldelem, typeof(TMethodDef)); // load method def (this.proxyMethods[methodDefIndex])

            bool isSingleton = operationInfo.Service.IsSingleton;

            Type[] reqestTypeCtorArgs = new Type[operationInfo.RequestParameters.Length + (isSingleton ? 0 : 1)];
            int argIndex = 0;

            if (!isSingleton)
            {
                il.Emit(OpCodes.Ldarg_0);// Load this (for objectId field)
                il.Emit(OpCodes.Ldfld, objectIdField); //Load objectId field
                reqestTypeCtorArgs[argIndex++] = typeof(RpcObjectId);
            }

            // Load parameters
            foreach (var requestParameter in operationInfo.RequestParameters)
            {
                RpcIlHelper.EmitLdArg(il, requestParameter.Index + 1);
                reqestTypeCtorArgs[argIndex++] = requestParameter.Type;
            }

            var ctorInfo = operationInfo.RequestType.GetConstructor(reqestTypeCtorArgs)
                ?? throw new NotImplementedException($"Request type constructor not found");
            il.Emit(OpCodes.Newobj, ctorInfo);  // new RpcRequestType<>( objectId, ...)

            MethodInfo callUnaryMethodInfo;
            if (operationInfo.ReturnType != typeof(void))
            {
                string callerMethodName = RpcProxyBase<TMethodDef>.CallUnaryMethodName;

                var callUnaryMethodDefInfo = GetProxyMethod(callerMethodName);
                callUnaryMethodInfo = callUnaryMethodDefInfo.MakeGenericMethod(operationInfo.RequestType, operationInfo.ResponseReturnType, operationInfo.ReturnType);

                EmitResponseConverter(il, operationInfo);
            }
            else
            {
                string callerMethodName = RpcProxyBase<TMethodDef>.CallUnaryVoidMethodName;
                var callUnaryMethodDefInfo = GetProxyMethod(callerMethodName);
                callUnaryMethodInfo = callUnaryMethodDefInfo.MakeGenericMethod(operationInfo.RequestType);
            }

            if (operationInfo.CancellationTokenIndex != null)
            {
                RpcIlHelper.EmitLdArg(il, operationInfo.CancellationTokenIndex.Value + 1);
            }
            else
            {
                // Load CancellationToken.None
                var ctLocal = il.DeclareLocal(typeof(CancellationToken));
                il.Emit(OpCodes.Ldloca_S, ctLocal);
                il.Emit(OpCodes.Initobj, typeof(CancellationToken));
                Debug.Assert(ctLocal.LocalIndex == 0);
                il.Emit(OpCodes.Ldloc_0);//, ctLocal.LocalIndex);
            }

            il.Emit(OpCodes.Call, callUnaryMethodInfo);
            il.Emit(OpCodes.Ret); //return
        }

        private void CreateEventAddHandler(RpcEventInfo eventInfo, int eventMethodIndex)
        {
            if (this.typeBuilder == null)
            {
                throw new InvalidOperationException();
            }

            var eventHandlerType = eventInfo.Event.EventHandlerType ?? throw new InvalidOperationException("EventHandlerType not defined.");
            var addImplMethodBuilder = this.typeBuilder.DefineMethod($"{eventInfo.Service.FullName}.add_{eventInfo.Name}", MethodAttributes.Private | MethodAttributes.Virtual,
                returnType: typeof(void),
                parameterTypes: new Type[] { eventHandlerType });

            var addMethod = eventInfo.Event.AddMethod ?? throw new InvalidOperationException("Event Add method not defined.");
            this.typeBuilder.DefineMethodOverride(addImplMethodBuilder, addMethod);
            var il = addImplMethodBuilder.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);   // Load this (for AddEventHandlerAsync arg)
            il.Emit(OpCodes.Ldarg_1);   // Load EventHandler
            RpcIlHelper.EmitLdc_I4(il, eventMethodIndex);    //

            var addHandlerMethodDef = GetProxyMethod(RpcProxyBase<TMethodDef>.AddEventHandlerAsyncName);
            var addHandlerMethod = addHandlerMethodDef.MakeGenericMethod(eventHandlerType, eventInfo.EventArgsType);
            il.Emit(OpCodes.Call, addHandlerMethod); // Call AddEventHandlerAsync(eventHandler, eventMethodIndex)
            il.Emit(OpCodes.Pop);   // Pop return (ignore returned Task)
            il.Emit(OpCodes.Ret);
        }

        /// <remarks>
        /// Assuming the service interface:
        /// <code><![CDATA[
        /// [RpcService]
        /// namespace TestSrvices
        /// {
        /// TODO: Document
        /// }
        /// ]]></code>
        /// This method will generate the method for the ... event (explicitly implemented):
        /// <code><![CDATA[
        /// TODO: Document
        /// ]]></code>
        /// </remarks>
        private void CreateEventImpl(RpcEventInfo eventInfo)
        {
            int eventMethodIndex = this.CreateEventMethod(eventInfo);

            this.CreateEventAddHandler(eventInfo, eventMethodIndex);
            this.CreateEventRemoveHandler(eventInfo, eventMethodIndex);
        }

        private int CreateEventMethod(RpcEventInfo eventInfo)
        {
            if (this.eventDataFields.TryGetValue(eventInfo.FullName, out var methodDefField))
            {
                if (!Equals(methodDefField.RequestType, eventInfo.Event.EventHandlerType)
                    || !Equals(methodDefField.ResponseType, eventInfo.EventArgsType))
                {
                    throw new RpcDefinitionException("Different versions of an RPC event must have the same request and response types.");
                }

                return methodDefField.Index;
            }

            var eventHandlerType = eventInfo.Event.EventHandlerType ?? throw new InvalidOperationException("EventHandlerType not defined.");

            int eventMethodDefIndex = this.AddMethodDef(
                eventInfo,
                $"Begin{eventInfo.Name}",
                RpcMethodType.EventAdd,
                typeof(RpcObjectRequest),
                eventInfo.EventArgsType,
                null,
                RpcClientFaultHandler.Empty);

            this.eventDataFields.Add(eventInfo.FullName, new MethodDefIndex(eventMethodDefIndex, eventHandlerType, eventInfo.EventArgsType));
            return eventMethodDefIndex;
        }

        private void CreateEventRemoveHandler(RpcEventInfo eventInfo, int eventMethodIndex)
        {
            if (this.typeBuilder == null || this.staticCtorILGenerator == null)
            {
                throw new InvalidOperationException();
            }

            var eventHandlerType = eventInfo.Event.EventHandlerType ?? throw new InvalidOperationException("EventHandlerType not defined.");
            var removeImplMethodBuilder = this.typeBuilder.DefineMethod($"{eventInfo.Service.FullName}.remove_{eventInfo.Name}", MethodAttributes.Private | MethodAttributes.Virtual,
                returnType: typeof(void),
                parameterTypes: new Type[] { eventHandlerType });

            var removeMethod = eventInfo.Event.RemoveMethod ?? throw new InvalidOperationException("Event Add method not defined.");
            this.typeBuilder.DefineMethodOverride(removeImplMethodBuilder, removeMethod);
            var il = removeImplMethodBuilder.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);   // Load this (for RemoveEventHandler arg)
            il.Emit(OpCodes.Ldarg_1);   // Load EventHandler
            RpcIlHelper.EmitLdc_I4(il, eventMethodIndex);

            var openRemoveHandlerMethod = GetProxyMethod(RpcProxyBase<TMethodDef>.RemoveEventHandlerAsyncName);
            var removeHandlerMethod = openRemoveHandlerMethod.MakeGenericMethod(eventHandlerType, eventInfo.EventArgsType);
            il.Emit(OpCodes.Call, removeHandlerMethod); // Call RemoveEventHandlerAsync(eventHandler, eventData)
            il.Emit(OpCodes.Pop);   // Pop return (ignore returned Task)
            il.Emit(OpCodes.Ret);
        }

        private int CreateMethodDefinitionField(RpcOperationInfo operationInfo,
                    RpcClientFaultHandler serviceFaultHandler,
                    RpcMemberInfo? serverSideMemberInfo)
        {
            if (this.typeBuilder == null)
            {
                throw new InvalidOperationException();
            }

            string operationName = operationInfo.FullName;

            if (this.methodDefinitionIndices.TryGetValue(operationName, out var methodDefField))
            {
                if (!Equals(methodDefField.RequestType, operationInfo.RequestType)
                    || !Equals(methodDefField.ResponseType, operationInfo.ResponseType))
                {
                    throw new RpcDefinitionException("Different versions of an RPC operation must have the same request and response types.");
                }

                return methodDefField.Index;
            }

            RpcClientFaultHandler faultHandler = RetrieveClientFaultHandler(operationInfo, serviceFaultHandler, serverSideMemberInfo);

            int methodDefIndex = this.AddMethodDef(
                operationInfo,
                RpcMethodType.Unary,
                null,
                faultHandler);

            this.methodDefinitionIndices.Add(operationName, new MethodDefIndex(methodDefIndex, operationInfo.RequestType, operationInfo.ResponseType));
            return methodDefIndex;
        }

        /// <summary>
        /// Creates a
        /// </summary>
        /// <remarks>
        /// Assuming the service interface:
        /// <code><![CDATA[
        /// [RpcService]
        /// namespace TestServices
        /// {
        ///     public interface ISequenceService
        ///     {
        ///         IAsyncEnumerable<SequenceData> GetSequenceAsEnumerable(int count, CancellationToken cancellationToken);
        ///     }
        /// }
        /// ]]></code>
        /// This method will generate the method for the Add operation (explicitly implemented):
        /// <code><![CDATA[
        /// IAsyncEnumerable<SequenceData> ISequenceService.GetSequenceAsEnumerable(int count, CancellationToken cancellationToken )
        /// {
        ///     TMethodDef methodDef = this.proxyMethods[<Index_ISequenceService.GetSequenceAsEnumerable>];
        ///
        ///     return CallAsyncEnumerableMethod<RpcObjectRequest<int>,SequenceData,SequenceData>(
        ///         methodDef,
        ///         new RpcObjectRequest<int>( this.objectId, count ),
        ///         null,   // response converter
        ///         default // cancellation token
        ///         );
        /// }
        /// ]]></code>
        /// </remarks>
        /// <returns></returns>
        private void CreateServerStreamingMethodImpl(RpcOperationInfo operationInfo, RpcClientFaultHandler serviceFaultHandler, RpcMemberInfo? serverSideMemberInfo)
        {
            if (this.typeBuilder == null)
            {
                throw new InvalidOperationException();
            }

            var implMethodBuilder = this.typeBuilder.DefineMethod($"{operationInfo.Service.Name}.{operationInfo.Method.Name}", MethodAttributes.Private | MethodAttributes.Virtual,
                returnType: operationInfo.Method.ReturnType,
                parameterTypes: operationInfo.Method.GetParameters().Select(p => p.ParameterType).ToArray());

            this.typeBuilder.DefineMethodOverride(implMethodBuilder, operationInfo.Method);

            var il = implMethodBuilder.GetILGenerator();

            var objectIdField = GetProxyField(RpcProxyBase<TMethodDef>.ObjectIdFieldName);
            var proxyMethodsField = GetProxyField(RpcProxyBase<TMethodDef>.ProxyMethodsFieldName);
            int methodDefIndex = this.CreateMethodDefinitionField(operationInfo, serviceFaultHandler, serverSideMemberInfo);

            il.Emit(OpCodes.Ldarg_0);// Load this
            il.Emit(OpCodes.Ldarg_0);// Load this (for proxyMethods field )
            il.Emit(OpCodes.Ldfld, proxyMethodsField); //Load method def field
            il.Emit(OpCodes.Ldc_I4, methodDefIndex);
            il.Emit(OpCodes.Ldelem, typeof(TMethodDef)); // load method def (this.proxyMethods[methodDefIndex])

            bool isSingleton = operationInfo.Service.IsSingleton;

            Type[] reqestTypeCtorArgs = new Type[operationInfo.RequestParameters.Length + (isSingleton ? 0 : 1)];
            int argIndex = 0;

            if (!isSingleton)
            {
                il.Emit(OpCodes.Ldarg_0);// Load this (for objectId field)
                il.Emit(OpCodes.Ldfld, objectIdField); //Load objectId field
                reqestTypeCtorArgs[argIndex++] = typeof(RpcObjectId);
            }

            // Load parameters
            foreach (var requestParameter in operationInfo.RequestParameters)
            {
                RpcIlHelper.EmitLdArg(il, requestParameter.Index + 1);
                reqestTypeCtorArgs[argIndex++] = requestParameter.Type;
            }

            var ctorInfo = operationInfo.RequestType.GetConstructor(reqestTypeCtorArgs)
                ?? throw new NotImplementedException($"Request type constructor not found");
            il.Emit(OpCodes.Newobj, ctorInfo);  // new RpcRequestType<>( objectId, ...)

            MethodInfo callUnaryMethodInfo;
            string callerMethodName = RpcProxyBase<TMethodDef>.CallAsyncEnumerableMethodName;
            var callUnaryMethodDefInfo = GetProxyMethod(callerMethodName);
            callUnaryMethodInfo = callUnaryMethodDefInfo.MakeGenericMethod(operationInfo.RequestType, operationInfo.ResponseReturnType, operationInfo.ReturnType);

            EmitResponseConverter(il, operationInfo);

            if (operationInfo.CancellationTokenIndex != null)
            {
                RpcIlHelper.EmitLdArg(il, operationInfo.CancellationTokenIndex.Value + 1);
            }
            else
            {
                // Load CancellationToken.None
                var ctLocal = il.DeclareLocal(typeof(CancellationToken));
                il.Emit(OpCodes.Ldloca_S, ctLocal);
                il.Emit(OpCodes.Initobj, typeof(CancellationToken));
                Debug.Assert(ctLocal.LocalIndex == 0);
                il.Emit(OpCodes.Ldloc_0);//, ctLocal.LocalIndex);
            }

            il.Emit(OpCodes.Call, callUnaryMethodInfo);
            il.Emit(OpCodes.Ret); //return
        }

        //private void CreatePropertyGetMethod(RpcPropertyInfo rpcPropertyInfo, IReadOnlyList<Expression> serviceFaultGeneratorExpressions, RpcMemberInfo? serverSideMemberInfo)
        //{
        //    var propertyInfo = rpcPropertyInfo.Property;
        //    if (propertyInfo.GetMethod != null)
        //    {
        //        this.CreateBlockingMethodImpl(new RpcOperationInfo
        //        (
        //            service: rpcPropertyInfo.Service,
        //            name: $"Get{propertyInfo.Name}",
        //            method: propertyInfo.GetMethod,
        //            requestType: typeof(RpcObjectRequest),
        //            requestTypeCtorArgTypes: ImmutableArray.Create(typeof(RpcObjectId)),
        //            methodType: RpcMethodType.Unary,
        //            isAsync: false,
        //            parametersCount: 0,
        //            responseType: typeof(RpcResponse<>).MakeGenericType(rpcPropertyInfo.ResponseReturnType),
        //            responseReturnType: rpcPropertyInfo.ResponseReturnType,
        //            returnType: propertyInfo.PropertyType,
        //            returnKind: rpcPropertyInfo.PropertyTypeKind
        //        ),
        //        serviceFaultGeneratorExpressions,
        //        serverSideMemberInfo
        //        );
        //    }
        //}
        //private void CreatePropertyImpl(RpcPropertyInfo propertyInfo, IReadOnlyList<Expression> serviceFaultGeneratorExpressions, RpcMemberInfo? serverSidePropertyInfo)
        //{
        //    //FieldInfo eventDataField = this.CreateEventDataField(eventInfo);
        //    this.CreatePropertyGetMethod(propertyInfo, serviceFaultGeneratorExpressions, serverSidePropertyInfo);
        //    this.CreatePropertySetMethod(propertyInfo, serviceFaultGeneratorExpressions, serverSidePropertyInfo);
        //    //this.CreateEventRemoveHandler(eventInfo, eventDataField);
        //}
        //private void CreatePropertySetMethod(RpcPropertyInfo rpcPropertyInfo, IReadOnlyList<Expression> serviceFaultGeneratorExpressions, RpcMemberInfo? serverSideMemberInfo)
        //{
        //    var propertyInfo = rpcPropertyInfo.Property;
        //    if (propertyInfo.SetMethod != null)
        //    {
        //        if (rpcPropertyInfo.PropertyTypeKind != ServiceOperationReturnKind.Standard)
        //        {
        //            throw new RpcDefinitionException($"Type {rpcPropertyInfo.Property.PropertyType} is not valid for RPC property setter '{rpcPropertyInfo.Name}'.");
        //        }
        //        this.CreateBlockingMethodImpl(new RpcOperationInfo
        //        (
        //            service: rpcPropertyInfo.Service,
        //            name: $"Set{propertyInfo.Name}",
        //            method: propertyInfo.SetMethod,
        //            requestType: typeof(RpcObjectRequest<>).MakeGenericType(propertyInfo.PropertyType),
        //            requestTypeCtorArgTypes: ImmutableArray.Create<Type>(typeof(RpcObjectId), propertyInfo.PropertyType),
        //            methodType: RpcMethodType.Unary,
        //            isAsync: false,
        //            parametersCount: 1,
        //            responseType: typeof(RpcResponse),
        //            returnType: typeof(void),
        //            responseReturnType: typeof(void),
        //            returnKind: ServiceOperationReturnKind.Standard
        //        ),
        //        serviceFaultGeneratorExpressions,
        //        serverSideMemberInfo);
        //    }
        //}

        private (TypeBuilder, ILGenerator) CreateTypeBuilder(RpcServiceInfo serviceInfo, Type[] proxyCtorArgs)
        {
            string proxyTypeName = this.GetProxyTypeName($"{serviceInfo.Namespace}.__{serviceInfo.Name}_Proxy");
            var typeBuilder = this.moduleBuilder.DefineType(proxyTypeName, TypeAttributes.Public | TypeAttributes.BeforeFieldInit, typeof(TRpcProxyBase));

            // Static constructor
            var staticCtorBuilder = typeBuilder.DefineTypeInitializer();
            // Generate call to base class
            var staticCtorIL = staticCtorBuilder.GetILGenerator();

            // Constructor
            var ctorBuilder = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, proxyCtorArgs);
            // Generate call to base class
            var ctorIL = ctorBuilder.GetILGenerator();
            RpcIlHelper.EmitLdArg(ctorIL, 0);    // Load this
            RpcIlHelper.EmitLdArg(ctorIL, 1);    // Load TRpcProxyArgs
            RpcIlHelper.EmitLdArg(ctorIL, 2);    // Load proxyMethodsCreator

            var baseCtor = typeof(TRpcProxyBase).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, proxyCtorArgs, null)
                ?? throw new NotImplementedException("Proxy ctor not found");
            ctorIL.Emit(OpCodes.Call, baseCtor);
            ctorIL.Emit(OpCodes.Ret);

            //// Factory method
            //var createMethodBuilder = typeBuilder.DefineMethod(RpcProxyBase<TMethodDef>.CreateMethodName, MethodAttributes.Public | MethodAttributes.Static, typeBuilder, proxyCtorArgs);
            //var createIL = createMethodBuilder.GetILGenerator();

            //EmitLdArg(createIL, 0);    // Load GrpcProxyArgs
            //createIL.Emit(OpCodes.Newobj, ctorBuilder);
            //createIL.Emit(OpCodes.Ret);

            return (typeBuilder, staticCtorIL);
        }

        private void EndStaticCtorILGenerator()
        {
            if (this.staticCtorILGenerator == null)
            {
                throw new InvalidOperationException();
            }

            this.staticCtorILGenerator.Emit(OpCodes.Ret);
            this.staticCtorILGenerator = null;
        }

        private string GetProxyTypeName(string baseName)
        {
            if (this.definedProxyTypes.TryGetValue(baseName, out int proxyCount))
            {
                int newCount = proxyCount + 1;
                this.definedProxyTypes[baseName] = newCount;

                return baseName + newCount.ToString(CultureInfo.InvariantCulture);
            }

            this.definedProxyTypes[baseName] = 1;
            return baseName;
        }

        private class MethodDefIndex
        {
            internal readonly int Index;

            internal readonly Type RequestType;

            internal readonly Type ResponseType;

            internal MethodDefIndex(int index, Type requestType, Type responseType)
            {
                this.Index = index;
                this.RequestType = requestType;
                this.ResponseType = responseType;
            }
        }
    }
}