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
using System.Collections.Immutable;
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
    /// Helper class for <see cref="RpcProxyProvider"/> which is used to create the actual proxy factories.
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
        /// <param name="proxyType">Proxy type created by <see cref=" BuildObjectProxyType"/>.</param>
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
        /// <returns></returns>
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

        private static Expression CreateMethodDefExpression(
            RpcMemberInfo memberInfo,
            string methodName,
            RpcMethodType methodType,
            Type requestType,
            Type responseType,
            Expression serializerParameter,
            Expression faultHandlerExpression)
        {
            var createMethodDefMethodDef = typeof(TRpcProxyBase).GetMethod(RpcProxyBase<TMethodDef>.CreateMethodDefName, BindingFlags.Static | BindingFlags.Public);
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
                string faultCode = faultAttribute.FaultCode;

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
                            var elementType = operationInfo.ReturnType.GetElementType();
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
                            var elementType = operationInfo.ReturnType.GetElementType().GenericTypeArguments[0];
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

        private static Expression RetrieveFaultHandlerExpression(RpcOperationInfo operationInfo, IReadOnlyList<Expression> faultConverterExpressions)
        {
            var faultAttributes = operationInfo.Method.GetCustomAttributes(typeof(RpcFaultAttribute));
            List<Expression> faultGeneratorExpressions = RetrieveRpcFaultGeneratorExpressions(faultAttributes);

            Expression faultHandlerExpression;
            if (faultGeneratorExpressions.Count > 0 || faultConverterExpressions.Count > 0)
            {
                var faultHandlerCtor = typeof(RpcClientFaultHandler)
                    .GetConstructor(new Type[] { typeof(IRpcClientExceptionConverter[]) });

                faultGeneratorExpressions.AddRange(faultConverterExpressions);

                // TODO: This expression should be stored in a field an reused for each 
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
            var handledMembers = new HashSet<MemberInfo>();

            var faultAttributes = serviceInfo.Type.GetCustomAttributes(typeof(RpcFaultAttribute));
            var serviceFaultGeneratorExpressions = RetrieveRpcFaultGeneratorExpressions(faultAttributes);

            var events = serviceInfo.Type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var eventInfo in events)
            {
                var rpcEventInfo = RpcBuilderUtil.GetEventInfoFromEvent(serviceInfo, eventInfo);
                this.CreateEventImpl(rpcEventInfo);

                handledMembers.Add(rpcEventInfo.Event.AddMethod);
                handledMembers.Add(rpcEventInfo.Event.RemoveMethod);
            }

            var properties = serviceInfo.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var propertyInfo in properties)
            {
                var rpcPropertyInfo = RpcBuilderUtil.GetPropertyInfoFromProperty(serviceInfo, propertyInfo);
                this.CreatePropertyImpl(rpcPropertyInfo, serviceFaultGeneratorExpressions);

                handledMembers.Add(rpcPropertyInfo.Property.GetMethod);
                handledMembers.Add(rpcPropertyInfo.Property.SetMethod);
            }

            var methods = serviceInfo.Type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                if (handledMembers.Add(method))
                {
                    var opInfo = RpcBuilderUtil.GetOperationInfoFromMethod(serviceInfo, method);
                    switch (opInfo.MethodType)
                    {
                        case RpcMethodType.Unary:
                            if (opInfo.IsAsync)
                            {
                                this.CreateAsyncMethodImpl(opInfo, serviceFaultGeneratorExpressions);
                            }
                            else
                            {
                                this.CreateBlockingMethodImpl(opInfo, serviceFaultGeneratorExpressions);
                            }
                            break;
                        default:
                            throw new NotImplementedException();

                    }
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
        /// private static readonly GrpcCore.Method<RpcObjectRequest<int, int>,RpcResponse<int>> __Method_TestServices_SimpleService_Add;
        /// Task<int> ISimpleService.AddAsync(int a, int b)
        /// {
        ///     return CallUnaryMethodAsync<RpcObjectRequest<int, int>, int>(__Method_TestServices_SimpleService_Add, new RpcObjectRequest<int, int>( this.objectId, a, b ), "TestServices.SimpleService", "Add");
        /// }
        /// ]]></code>
        /// </remarks>
        /// <returns></returns>
        private void CreateAsyncMethodImpl(RpcOperationInfo operationInfo, IReadOnlyList<Expression> serviceFaultGeneratorExpressions)
        {
            if (this.typeBuilder == null)
            {
                throw new InvalidOperationException();
            }

            Type taskReturnType;
            if (operationInfo.ReturnType != typeof(void))
            {
                taskReturnType = typeof(Task<>).MakeGenericType(operationInfo.ReturnType);
            }
            else
            {
                taskReturnType = typeof(Task);
            }

            var implMethodBuilder = this.typeBuilder.DefineMethod($"{operationInfo.Service.Name}.{operationInfo.Method.Name}", MethodAttributes.Private | MethodAttributes.Virtual,
                returnType: operationInfo.Method.ReturnType,
                parameterTypes: operationInfo.Method.GetParameters().Select(p => p.ParameterType).ToArray());

            this.typeBuilder.DefineMethodOverride(implMethodBuilder, operationInfo.Method);

            var il = implMethodBuilder.GetILGenerator();

            var objectIdField = typeof(TRpcProxyBase).GetField(RpcProxyBase<TMethodDef>.ObjectIdFieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            var proxyMethodsField = typeof(TRpcProxyBase).GetField(RpcProxyBase<TMethodDef>.ProxyMethodsFieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            il.Emit(OpCodes.Ldarg_0);// Load this

            int methodDefIndex = this.CreateMethodDefinitionField(operationInfo, serviceFaultGeneratorExpressions);

            il.Emit(OpCodes.Ldarg_0);// Load this (for proxyMethods field )
            il.Emit(OpCodes.Ldfld, proxyMethodsField); //Load method def field
            il.Emit(OpCodes.Ldc_I4, methodDefIndex);
            il.Emit(OpCodes.Ldelem, typeof(TMethodDef)); // load method def (this.proxyMethods[methodDefIndex])

            il.Emit(OpCodes.Ldarg_0);// Load this (for objectId field)
            il.Emit(OpCodes.Ldfld, objectIdField); //Load objectId field

            if (operationInfo.ParametersCount != operationInfo.Method.GetParameters().Length)
            {
                throw new NotImplementedException("Special parameters such as CancellationToken not yet implemented.");
            }

            // Load parameters
            for (int parameterIndex = 0; parameterIndex < operationInfo.ParametersCount; parameterIndex++)
            {
                RpcIlHelper.EmitLdArg(il, parameterIndex + 1);
            }

            var ctorInfo = operationInfo.RequestType.GetConstructor(operationInfo.RequestTypeCtorArgTypes.ToArray());
            il.Emit(OpCodes.Newobj, ctorInfo);  // new RpcRequestType<>( objectId, ...)

            MethodInfo callUnaryMethodInfo;
            if (operationInfo.ReturnType != typeof(void))
            {
                string callerMethodName = RpcProxyBase<TMethodDef>.CallUnaryMethodAsyncName;

                var callUnaryMethodDefInfo = typeof(TRpcProxyBase).GetMethod(callerMethodName, BindingFlags.NonPublic | BindingFlags.Instance);
                callUnaryMethodInfo = callUnaryMethodDefInfo.MakeGenericMethod(operationInfo.RequestType, operationInfo.ResponseReturnType, operationInfo.ReturnType);

                EmitResponseConverter(il, operationInfo);
            }
            else
            {
                var callUnaryMethodDefInfo = typeof(TRpcProxyBase).GetMethod(RpcProxyBase<TMethodDef>.CallUnaryVoidMethodAsyncName, BindingFlags.NonPublic | BindingFlags.Instance);
                callUnaryMethodInfo = callUnaryMethodDefInfo.MakeGenericMethod(operationInfo.RequestType);
            }

            // TODO: Cancellation token. Currently always CancellationToken.None
            var ctLocal = il.DeclareLocal(typeof(CancellationToken));
            il.Emit(OpCodes.Ldloca_S, ctLocal);
            il.Emit(OpCodes.Initobj, typeof(CancellationToken));
            Debug.Assert(ctLocal.LocalIndex == 0);
            il.Emit(OpCodes.Ldloc_0);//, ctLocal.LocalIndex);


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
        /// namespace TestSrvices
        /// {
        ///     public interface IBlockingService
        ///     {
        ///         int Add(int a, int b);
        ///     }
        /// }
        /// ]]></code>
        /// This method will generate the method for the AddAsync operation (explicitly implemented):
        /// <code><![CDATA[
        /// private static readonly GrpcCore.Method<RpcObjectRequest<int, int>,RpcResponse<int>> __Method_TestServices_SimpleService_Add;
        /// int IBlockingService.Add(int a, int b)
        /// {
        ///     return CallUnaryMethod<RpcObjectRequest<int, int>>(
        ///         __Method_TestServices_SimpleService_Add, 
        ///         new RpcObjectRequest<int, int>( this.objectId, a, b ), 
        ///         "TestServices.SimpleService", "Add");
        /// }
        /// ]]></code>
        /// </remarks>
        /// <returns></returns>
        private void CreateBlockingMethodImpl(RpcOperationInfo operationInfo, IReadOnlyList<Expression> serviceFaultGeneratorExpressions)
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

            var objectIdField = typeof(TRpcProxyBase).GetField(RpcProxyBase<TMethodDef>.ObjectIdFieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            var proxyMethodsField = typeof(TRpcProxyBase).GetField(RpcProxyBase<TMethodDef>.ProxyMethodsFieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            int methodDefIndex = this.CreateMethodDefinitionField(operationInfo, serviceFaultGeneratorExpressions);

            il.Emit(OpCodes.Ldarg_0);// Load this
            il.Emit(OpCodes.Ldarg_0);// Load this (for proxyMethods field )
            il.Emit(OpCodes.Ldfld, proxyMethodsField); //Load method def field
            il.Emit(OpCodes.Ldc_I4, methodDefIndex);
            il.Emit(OpCodes.Ldelem, typeof(TMethodDef)); // load method def (this.proxyMethods[methodDefIndex])

            il.Emit(OpCodes.Ldarg_0);// Load this (for objectId field)
            il.Emit(OpCodes.Ldfld, objectIdField); //Load objectId field

            if (operationInfo.ParametersCount != operationInfo.Method.GetParameters().Length)
            {
                throw new NotImplementedException("Special parameters such as CancellationToken not yet implemented.");
            }

            // Load parameters
            for (int parameterIndex = 0; parameterIndex < operationInfo.ParametersCount; parameterIndex++)
            {
                RpcIlHelper.EmitLdArg(il, parameterIndex + 1);
            }

            var ctorInfo = operationInfo.RequestType.GetConstructor(operationInfo.RequestTypeCtorArgTypes.ToArray());
            il.Emit(OpCodes.Newobj, ctorInfo);  // new RpcRequestType<>( objectId, ...)


            MethodInfo callUnaryMethodInfo;
            if (operationInfo.ReturnType != typeof(void))
            {
                string callerMethodName = RpcProxyBase<TMethodDef>.CallUnaryMethodName;

                var callUnaryMethodDefInfo = typeof(TRpcProxyBase).GetMethod(callerMethodName, BindingFlags.NonPublic | BindingFlags.Instance);
                callUnaryMethodInfo = callUnaryMethodDefInfo.MakeGenericMethod(operationInfo.RequestType, operationInfo.ResponseReturnType, operationInfo.ReturnType);

                EmitResponseConverter(il, operationInfo);
            }
            else
            {
                var callUnaryMethodDefInfo = typeof(TRpcProxyBase).GetMethod(RpcProxyBase<TMethodDef>.CallUnaryVoidMethodName, BindingFlags.NonPublic | BindingFlags.Instance);
                callUnaryMethodInfo = callUnaryMethodDefInfo.MakeGenericMethod(operationInfo.RequestType);

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

            var addImplMethodBuilder = this.typeBuilder.DefineMethod($"{eventInfo.Service.FullName}.add_{eventInfo.Name}", MethodAttributes.Private | MethodAttributes.Virtual,
                returnType: typeof(void),
                parameterTypes: new Type[] { eventInfo.Event.EventHandlerType });

            this.typeBuilder.DefineMethodOverride(addImplMethodBuilder, eventInfo.Event.AddMethod);
            var il = addImplMethodBuilder.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);   // Load this (for AddEventHandlerAsync arg)
            il.Emit(OpCodes.Ldarg_1);   // Load EventHandler
            RpcIlHelper.EmitLdc_I4(il, eventMethodIndex);    // 

            var addHandlerMethodDef = typeof(TRpcProxyBase).GetMethod(RpcProxyBase<TMethodDef>.AddEventHandlerAsyncName, BindingFlags.Instance | BindingFlags.NonPublic);
            var addHandlerMethod = addHandlerMethodDef.MakeGenericMethod(eventInfo.Event.EventHandlerType, eventInfo.EventArgsType);
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

            var eventDataType = typeof(RpcProxyBase<>.EventData<>).MakeGenericType(typeof(TMethodDef), eventInfo.Event.EventHandlerType);

            int eventMethodDefIndex = this.AddCreateMethodDefExpression(
                eventInfo,
                $"Begin{eventInfo.Name}",
                RpcMethodType.EventAdd,
                typeof(RpcObjectRequest),
                eventInfo.EventArgsType,
                NullSerializerExpression,
                NullFaultHandlerExpression);

            this.eventDataFields.Add(eventInfo.FullName, new MethodDefIndex(eventMethodDefIndex, eventInfo.Event.EventHandlerType, eventInfo.EventArgsType));
            return eventMethodDefIndex;
        }

        private void CreateEventRemoveHandler(RpcEventInfo eventInfo, int eventMethodIndex)
        {
            if (this.typeBuilder == null || this.staticCtorILGenerator == null)
            {
                throw new InvalidOperationException();
            }

            var removeImplMethodBuilder = this.typeBuilder.DefineMethod($"{eventInfo.Service.FullName}.remove_{eventInfo.Name}", MethodAttributes.Private | MethodAttributes.Virtual,
                returnType: typeof(void),
                parameterTypes: new Type[] { eventInfo.Event.EventHandlerType });

            this.typeBuilder.DefineMethodOverride(removeImplMethodBuilder, eventInfo.Event.RemoveMethod);
            var il = removeImplMethodBuilder.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);   // Load this (for RemoveEventHandler arg)
            il.Emit(OpCodes.Ldarg_1);   // Load EventHandler
            RpcIlHelper.EmitLdc_I4(il, eventMethodIndex);

            var openRemoveHandlerMethod = typeof(TRpcProxyBase).GetMethod(RpcProxyBase<TMethodDef>.RemoveEventHandlerAsyncName, BindingFlags.Instance | BindingFlags.NonPublic);
            var removeHandlerMethod = openRemoveHandlerMethod.MakeGenericMethod(eventInfo.Event.EventHandlerType, eventInfo.EventArgsType);
            il.Emit(OpCodes.Call, removeHandlerMethod); // Call RemoveEventHandlerAsync(eventHandler, eventData)
            il.Emit(OpCodes.Pop);   // Pop return (ignore returned Task)
            il.Emit(OpCodes.Ret);
        }

        private int CreateMethodDefinitionField(RpcOperationInfo operationInfo, IReadOnlyList<Expression> serviceFaultGeneratorExpressions)
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

            Expression faultHandlerExpression = RetrieveFaultHandlerExpression(operationInfo, serviceFaultGeneratorExpressions);

            int methodDefIndex = this.AddCreateMethodDefExpression(
                operationInfo,
                RpcMethodType.Unary,
                NullSerializerExpression,
                faultHandlerExpression);

            this.methodDefinitionIndices.Add(operationName, new MethodDefIndex(methodDefIndex, operationInfo.RequestType, operationInfo.ResponseType));
            return methodDefIndex;
        }

        private void CreatePropertyGetMethod(RpcPropertyInfo rpcPropertyInfo, IReadOnlyList<Expression> serviceFaultGeneratorExpressions)
        {
            var propertyInfo = rpcPropertyInfo.Property;
            if (propertyInfo.GetMethod != null)
            {
                this.CreateBlockingMethodImpl(new RpcOperationInfo
                (
                    service: rpcPropertyInfo.Service,
                    name: $"Get{propertyInfo.Name}",
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
                ),
                serviceFaultGeneratorExpressions
                );
            }
        }

        private void CreatePropertyImpl(RpcPropertyInfo propertyInfo, IReadOnlyList<Expression> serviceFaultGeneratorExpressions)
        {
            //FieldInfo eventDataField = this.CreateEventDataField(eventInfo);

            this.CreatePropertyGetMethod(propertyInfo, serviceFaultGeneratorExpressions);
            this.CreatePropertySetMethod(propertyInfo, serviceFaultGeneratorExpressions);
            //this.CreateEventRemoveHandler(eventInfo, eventDataField);
        }

        private void CreatePropertySetMethod(RpcPropertyInfo rpcPropertyInfo, IReadOnlyList<Expression> serviceFaultGeneratorExpressions)
        {
            var propertyInfo = rpcPropertyInfo.Property;
            if (propertyInfo.SetMethod != null)
            {
                if (rpcPropertyInfo.PropertyTypeKind != ServiceOperationReturnKind.Standard)
                {
                    throw new RpcDefinitionException($"Type {rpcPropertyInfo.Property.PropertyType} is not valid for RPC property setter '{rpcPropertyInfo.Name}'.");
                }

                this.CreateBlockingMethodImpl(new RpcOperationInfo
                (
                    service: rpcPropertyInfo.Service,
                    name: $"Set{propertyInfo.Name}",
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
                ),
                serviceFaultGeneratorExpressions);
            }
        }

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

            var baseCtor = typeof(TRpcProxyBase).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, proxyCtorArgs, null);
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
