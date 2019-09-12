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

using SciTech.Rpc.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Client.Internal
{
    /// <summary>
    /// Helper class for <see cref="RpcProxyGenerator"/> which is used to create the actual proxy factories.
    /// </summary>
    internal class RpcServiceProxyBuilder<TRpcProxyBase, TMethodDef>
        where TRpcProxyBase : RpcProxyBase<TMethodDef>
        where TMethodDef : RpcProxyMethod
    {
        //private static readonly Expression NullConverterExpression = Expression.Constant(null, typeof(Func<IRpcService, object, object>));
        private static readonly Expression NullFaultHandlerExpression = Expression.Constant(null, typeof(RpcClientFaultHandler));

        private static readonly Expression NullSerializerExpression = Expression.Constant(null, typeof(IRpcSerializer));

        private readonly Dictionary<string, int> definedProxyTypes;

        private readonly Dictionary<string, MethodDefIndex> eventDataFields = new Dictionary<string, MethodDefIndex>();

        private readonly Dictionary<string, MethodDefIndex> methodDefinitionIndices = new Dictionary<string, MethodDefIndex>();
        ///// <summary>
        ///// Only initialized during call to BuildObjectProxyType
        ///// </summary>
        //private ParameterExpression serializerExpression;

        private readonly ModuleBuilder moduleBuilder;

        private readonly IRpcProxyDefinitionsProvider proxyServicesProvider;

        private IReadOnlyList<RpcServiceInfo> allServices;

        /// <summary>
        /// Only initialized during call to BuildObjectProxyType. 
        /// </summary>
        private List<Expression>? createMethodDefExpressions;

        /// <summary>
        /// Only initialized during call to BuildObjectProxyType
        /// </summary>
        private ILGenerator? staticCtorILGenerator;

        /// <summary>
        /// Only initialized during call to BuildObjectProxyType
        /// </summary>
        private TypeBuilder? typeBuilder;

        internal RpcServiceProxyBuilder(
            IReadOnlyList<RpcServiceInfo> allServices, IRpcProxyDefinitionsProvider proxyServicesProvider,
            ModuleBuilder moduleBuilder, Dictionary<string, int> definedProxyTypes)
        {
            this.allServices = allServices;
            this.proxyServicesProvider = proxyServicesProvider;
            this.moduleBuilder = moduleBuilder;
            this.definedProxyTypes = definedProxyTypes;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TProxyArgs"></typeparam>
        /// <returns></returns>
        public (Func<TProxyArgs, TMethodDef[], RpcProxyBase>, Func<TMethodDef[]>) BuildObjectProxyFactory<TProxyArgs>() where TProxyArgs : RpcProxyArgs
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
            var proxyCtor = proxyType.GetConstructor(new Type[] { typeof(TProxyArgs), typeof(TMethodDef[]) });

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
        internal (Type, Func<TMethodDef[]>) BuildObjectProxyType(Type[] proxyCtorArgs)
        {
            var declaredServiceInfo = this.allServices.Single(s => s.IsDeclaredService);

            (this.typeBuilder, this.staticCtorILGenerator) = this.CreateTypeBuilder(declaredServiceInfo, proxyCtorArgs);
            this.createMethodDefExpressions = new List<Expression>();
            //this.serializerExpression = Expression.Parameter(typeof(IRpcSerializer), "serializer");

            foreach (var serviceInfo in this.allServices)
            {
                this.typeBuilder.AddInterfaceImplementation(serviceInfo.Type);
                this.AddServiceProxyMembers(serviceInfo);
            }

            this.EndStaticCtorILGenerator();
            var proxyType = this.typeBuilder.CreateTypeInfo();
            if (proxyType == null) throw new NotSupportedException("Failed to create proxy type.");

            var createProxyMethodsExpression =
                Expression.Lambda(
                    Expression.NewArrayInit(
                        typeof(TMethodDef),
                        this.createMethodDefExpressions
                        )
                    );
            var createMethodsFunc = (Func<TMethodDef[]>)createProxyMethodsExpression.Compile();

            this.typeBuilder = null;
            this.createMethodDefExpressions = null;
            this.methodDefinitionIndices.Clear();
            this.eventDataFields.Clear();

            // TODO:
            return (proxyType, createMethodsFunc);

        }


        private Expression CreateMethodDefExpression(
            RpcMemberInfo memberInfo,
            string methodName,
            RpcMethodType methodType,
            Type requestType,
            Type responseType,
            Expression serializerParameter,
            Expression faultHandlerExpression)
        {
            var createMethodDefMethodDef = GetProxyMethod(RpcProxyBase<TMethodDef>.CreateMethodDefName, BindingFlags.Static | BindingFlags.Public);
            var createMethodDefMethod = createMethodDefMethodDef.MakeGenericMethod(requestType, responseType);

            return Expression.Call(createMethodDefMethod,
                Expression.Constant(methodType),
                Expression.Constant(memberInfo.Service.FullName),
                Expression.Constant(methodName),
                serializerParameter,
                faultHandlerExpression);
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

        private static Expression GenerateFaultHandlerExpression(RpcFaultAttribute faultAttribute)
        {
            if (!string.IsNullOrWhiteSpace(faultAttribute.FaultCode))
            {
                _ = faultAttribute.FaultCode;

                if (faultAttribute.FaultType != null)
                {
                    var faultConverterType = typeof(RpcFaultExceptionConverter<>).MakeGenericType(faultAttribute.FaultType);
                    var defaultConverterField = faultConverterType.GetField(nameof(RpcFaultExceptionConverter<object>.Default), BindingFlags.Static | BindingFlags.Public);

                    var converterFieldExpression = Expression.Field(null, defaultConverterField);

                    return converterFieldExpression;
                }
                else
                {
                    var faultConverterType = typeof(RpcFaultExceptionConverter);
                    var converterCtor = faultConverterType.GetConstructor(new Type[] { typeof(string) });
                    var faultCodeExpression = Expression.Constant(faultAttribute.FaultCode);
                    var converterNewExpression = Expression.New(converterCtor, faultCodeExpression);

                    return converterNewExpression;
                }
            }

            throw new RpcDefinitionException("FaultCode must be specified in RpcFaultAttribute.");
        }

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

        private static IEnumerable<RpcFaultAttribute> RetrieveFaultAttributes(RpcOperationInfo operationInfo, RpcMemberInfo? serverSideMemberInfo)
        {
            // TODO: This is alsmost the same code as RetrieveServiceFaultAttributes, try to combine.
            List<RpcFaultAttribute> faultAttributes;

            if (serverSideMemberInfo?.DeclaringMember is MemberInfo serverSideMember)
            {
                // If the server side definition is available, then the fault attributes of that definition
                // should be used.
                faultAttributes = serverSideMember.GetCustomAttributes<RpcFaultAttribute>().ToList();

                // Validate that any fault attributes applied to the client side definition exists on the server side
                // definition
                foreach (var clientFaultAttribute in operationInfo.DeclaringMember.GetCustomAttributes<RpcFaultAttribute>())
                {
                    if (faultAttributes.Find(sa => sa.FaultCode == clientFaultAttribute.FaultCode && Equals(sa.FaultType, clientFaultAttribute.FaultType)) == null)
                    {
                        throw new RpcDefinitionException($"Client side service definition includes fault declaration '{clientFaultAttribute.FaultCode}' which is not applied on server side definition.");
                    }
                }

            }
            else
            {
                faultAttributes = operationInfo.DeclaringMember.GetCustomAttributes<RpcFaultAttribute>().ToList();
            }

            return faultAttributes;
        }

        private static Expression RetrieveFaultHandlerExpression(RpcOperationInfo operationInfo, IReadOnlyList<Expression> faultConverterExpressions, RpcMemberInfo? serverSideMemberInfo)
        {
            IEnumerable<Attribute> faultAttributes = RetrieveFaultAttributes(operationInfo, serverSideMemberInfo);
            List<Expression> faultGeneratorExpressions = RetrieveRpcFaultGeneratorExpressions(faultAttributes);

            Expression faultHandlerExpression;
            if (faultGeneratorExpressions.Count > 0 || faultConverterExpressions.Count > 0)
            {
                var faultHandlerCtor = typeof(RpcClientFaultHandler)
                    .GetConstructor(new Type[] { typeof(IRpcClientExceptionConverter[]) });

                faultGeneratorExpressions.AddRange(faultConverterExpressions);

                // TODO: This expression should be stored in a field and reused for each 
                // member that has the same fault specification. If service faults are
                // used, it's pretty likely that several members will have the same fault specifications.
                faultHandlerExpression = Expression.New(faultHandlerCtor,
                    Expression.NewArrayInit(typeof(IRpcClientExceptionConverter), faultGeneratorExpressions));
            }
            else
            {
                faultHandlerExpression = NullFaultHandlerExpression;
            }

            return faultHandlerExpression;
        }

        private static List<Expression> RetrieveRpcFaultGeneratorExpressions(IEnumerable<Attribute> faultAttributes)
        {
            var faultExceptionExpressions = new List<Expression>();
            foreach (RpcFaultAttribute faultAttribute in faultAttributes)
            {
                var faultExceptionExpression = GenerateFaultHandlerExpression(faultAttribute);
                faultExceptionExpressions.Add(faultExceptionExpression);
            }

            return faultExceptionExpressions;
        }

        private static IEnumerable<Attribute> RetrieveServiceFaultAttributes(RpcServiceInfo serviceInfo)
        {
            List<RpcFaultAttribute> faultAttributes;

            if (serviceInfo.ServerType != null)
            {
                // If the server side definition is available, then the fault attributes of that definition
                // should be used.
                faultAttributes = serviceInfo.ServerType.GetCustomAttributes<RpcFaultAttribute>().ToList();

                // Validate that any fault attributes applied to the client side definition exists on the server side
                // definition
                foreach (var clientFaultAttribute in serviceInfo.Type.GetCustomAttributes<RpcFaultAttribute>())
                {
                    if (faultAttributes.Find(sa => sa.FaultCode == clientFaultAttribute.FaultCode && Equals(sa.FaultType, clientFaultAttribute.FaultType)) == null)
                    {
                        throw new RpcDefinitionException($"Client side service definition includes fault declaration '{clientFaultAttribute.FaultCode}' which is not applied on server side definition.");
                    }
                }

            }
            else
            {
                faultAttributes = serviceInfo.Type.GetCustomAttributes<RpcFaultAttribute>().ToList();
            }

            return faultAttributes;
        }

        private int AddCreateMethodDefExpression(
            RpcOperationInfo memberInfo,
            RpcMethodType methodType,
            Expression serializerParameter,
            Expression faultHandlerExpression)
        {
            if (this.createMethodDefExpressions == null)
            {
                throw new InvalidOperationException();
            }

            int methodDefIndex = this.createMethodDefExpressions.Count;

            this.createMethodDefExpressions.Add(
                CreateMethodDefExpression(memberInfo, memberInfo.Name, methodType, memberInfo.RequestType, memberInfo.ResponseType,
                serializerParameter, faultHandlerExpression));
            return methodDefIndex;
        }

        private int AddCreateMethodDefExpression(
            RpcMemberInfo memberInfo,
            string methodName,
            RpcMethodType methodType,
            Type requestType,
            Type responseType,
            Expression serializerParameter,
            Expression faultHandlerExpression)
        {
            if (this.createMethodDefExpressions == null)
            {
                throw new InvalidOperationException();
            }

            int methodDefIndex = this.createMethodDefExpressions.Count;

            this.createMethodDefExpressions.Add(
                CreateMethodDefExpression(memberInfo, methodName, methodType, requestType, responseType,
                serializerParameter, faultHandlerExpression));
            return methodDefIndex;
        }

        /// <summary>
        /// Adds all declared members of the service interface specified by <paramref name="serviceInfo"/> to the proxy type 
        /// being built.
        /// </summary>
        private void AddServiceProxyMembers(RpcServiceInfo serviceInfo)
        {
            IEnumerable<Attribute> faultAttributes = RetrieveServiceFaultAttributes(serviceInfo);
            var serviceFaultGeneratorExpressions = RetrieveRpcFaultGeneratorExpressions(faultAttributes);

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
                                this.CreateAsyncMethodImpl(rpcMethodInfo, serviceFaultGeneratorExpressions, serverSideMemberInfo);
                            }
                            else
                            {
                                this.CreateBlockingMethodImpl(rpcMethodInfo, serviceFaultGeneratorExpressions, serverSideMemberInfo);
                            }
                            break;
                        case RpcMethodType.ServerStreaming:
                            this.CreateServerStreamingMethodImpl(rpcMethodInfo, serviceFaultGeneratorExpressions, serverSideMemberInfo);
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
        ///     return CallUnaryMethodAsync<RpcObjectRequest<int, int>, int>(methodDef, new RpcObjectRequest<int, int>( this.objectId, a, b ), "TestServices.SimpleService", "Add");
        /// }
        /// ]]></code>
        /// </remarks>
        /// <returns></returns>
        private void CreateAsyncMethodImpl(RpcOperationInfo operationInfo, IReadOnlyList<Expression> serviceFaultGeneratorExpressions, RpcMemberInfo? serverSideMethodInfo)
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
            il.Emit(OpCodes.Ldarg_0);// Load this

            int methodDefIndex = this.CreateMethodDefinitionField(operationInfo, serviceFaultGeneratorExpressions, serverSideMethodInfo);

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
            bool allowFault = operationInfo.AllowFault;

            if (operationInfo.ReturnType != typeof(void))
            {
                string callerMethodName = allowFault ? RpcProxyBase<TMethodDef>.CallUnaryMethodWithErrorAsyncName : RpcProxyBase<TMethodDef>.CallUnaryMethodAsyncName;

                var callUnaryMethodDefInfo = GetProxyMethod(callerMethodName);
                callUnaryMethodInfo = callUnaryMethodDefInfo.MakeGenericMethod(operationInfo.RequestType, operationInfo.ResponseReturnType, operationInfo.ReturnType);

                EmitResponseConverter(il, operationInfo);
            }
            else
            {
                string callerMethodName = allowFault ? RpcProxyBase<TMethodDef>.CallUnaryVoidMethodWithErrorAsyncName : RpcProxyBase<TMethodDef>.CallUnaryVoidMethodAsyncName;

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
        private void CreateBlockingMethodImpl(RpcOperationInfo operationInfo, IReadOnlyList<Expression> serviceFaultGeneratorExpressions, RpcMemberInfo? serverSideMemberInfo)
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
            int methodDefIndex = this.CreateMethodDefinitionField(operationInfo, serviceFaultGeneratorExpressions, serverSideMemberInfo);

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

            bool allowFault = operationInfo.AllowFault;

            MethodInfo callUnaryMethodInfo;
            if (operationInfo.ReturnType != typeof(void))
            {
                string callerMethodName = allowFault ? RpcProxyBase<TMethodDef>.CallUnaryMethodWithErrorName : RpcProxyBase<TMethodDef>.CallUnaryMethodName;

                var callUnaryMethodDefInfo = GetProxyMethod(callerMethodName);
                callUnaryMethodInfo = callUnaryMethodDefInfo.MakeGenericMethod(operationInfo.RequestType, operationInfo.ResponseReturnType, operationInfo.ReturnType);

                EmitResponseConverter(il, operationInfo);
            }
            else
            {
                string callerMethodName = allowFault ? RpcProxyBase<TMethodDef>.CallUnaryVoidMethodWithErrorName : RpcProxyBase<TMethodDef>.CallUnaryVoidMethodName;
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
        private void CreateServerStreamingMethodImpl(RpcOperationInfo operationInfo, IReadOnlyList<Expression> serviceFaultGeneratorExpressions, RpcMemberInfo? serverSideMemberInfo)
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
            int methodDefIndex = this.CreateMethodDefinitionField(operationInfo, serviceFaultGeneratorExpressions, serverSideMemberInfo);

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

            int eventMethodDefIndex = this.AddCreateMethodDefExpression(
                eventInfo,
                $"Begin{eventInfo.Name}",
                RpcMethodType.EventAdd,
                typeof(RpcObjectRequest),
                eventInfo.EventArgsType,
                NullSerializerExpression,
                NullFaultHandlerExpression);

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

        private int CreateMethodDefinitionField(RpcOperationInfo operationInfo, IReadOnlyList<Expression> serviceFaultGeneratorExpressions, RpcMemberInfo? serverSideMemberInfo)
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

            Expression faultHandlerExpression = RetrieveFaultHandlerExpression(operationInfo, serviceFaultGeneratorExpressions, serverSideMemberInfo);

            int methodDefIndex = this.AddCreateMethodDefExpression(
                operationInfo,
                RpcMethodType.Unary,
                NullSerializerExpression,
                faultHandlerExpression);

            this.methodDefinitionIndices.Add(operationName, new MethodDefIndex(methodDefIndex, operationInfo.RequestType, operationInfo.ResponseType));
            return methodDefIndex;
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
