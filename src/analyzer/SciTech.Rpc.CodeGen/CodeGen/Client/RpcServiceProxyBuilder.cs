using Microsoft.CodeAnalysis;
using SciTech.Rpc.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static System.FormattableString;

namespace SciTech.Rpc.CodeGen.Client
{
    internal class RpcServiceProxyBuilder
    {
        private readonly Dictionary<string, int> definedProxyTypes;

        private GeneratorExecutionContext generatorContext;

        private IReadOnlyList<RpcServiceInfo> allServices;
        private RpcBuilderUtil builder;
        private StringBuilder typeBuilder;

        internal RpcServiceProxyBuilder(
            IReadOnlyList<RpcServiceInfo> allServices,
            GeneratorExecutionContext generatorContext,
            //ModuleBuilder moduleBuilder, 
            Dictionary<string, int> definedProxyTypes)
        {
            this.allServices = allServices;
            this.generatorContext = generatorContext;
            this.definedProxyTypes = definedProxyTypes;
        }

        internal string BuildProxy()
        {
            var declaredService = allServices.Single(s => s.IsDeclaredService);
            this.builder = new RpcBuilderUtil(this.generatorContext.Compilation);

            CreateTypeBuilder(declaredService);
            foreach( var service in allServices)
            {
                AddServiceProxyMembers(service);
            }

            this.DecreaseIndent();
            this.AppendLine("}");
            this.DecreaseIndent();
            this.AppendLine("}");
            //this.DecreaseIndent();
            //this.AppendLine("}");

            return typeBuilder.ToString();
        }

        private StringBuilder CreateTypeBuilder(RpcServiceInfo serviceInfo)
        {
            string proxyTypeName = $"__{serviceInfo.Name}_Proxy";
            this.typeBuilder = new StringBuilder();// this.moduleBuilder.DefineType(proxyTypeName, TypeAttributes.Public | TypeAttributes.BeforeFieldInit, typeof(TRpcProxyBase));

            this.AppendLine($"namespace {serviceInfo.Namespace}");
            this.AppendLine("{");
            this.IncreaseIndent();
            this.Append($"internal class {proxyTypeName} : RpcProxyBase, ");

            bool firstService = true;

            foreach( var implementedService in this.allServices)
            {
                if( !firstService)
                {
                    this.Append(", ");
                }
                if( implementedService.Namespace != serviceInfo.Namespace )
                {
                    this.Append(implementedService.Namespace + ".");
                }

                this.Append(implementedService.Type.Name);
                firstService = false;
            }

            this.AppendLine();
            this.AppendLine("{");
            this.IncreaseIndent();
            this.AppendLine($"public {proxyTypeName}(SciTech.Rpc.Client.RpcProxyArgs proxyArgs, System.Collections.Immutable.ImmutableArray<SciTech.Rpc.RpcProxyMethod> methods) : base(proxyArgs, methods)");
            this.IncreaseIndent();
            this.AppendLine($": base(proxyArgs, methods)");
            this.DecreaseIndent();
            this.AppendLine("{");
            this.AppendLine("}");

            return typeBuilder;

            //// Static constructor
            //var staticCtorBuilder = typeBuilder.DefineTypeInitializer();
            //// Generate call to base class
            //var staticCtorIL = staticCtorBuilder.GetILGenerator();

            //// Constructor
            //var ctorBuilder = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, proxyCtorArgs);
            //// Generate call to base class
            //var ctorIL = ctorBuilder.GetILGenerator();
            //RpcIlHelper.EmitLdArg(ctorIL, 0);    // Load this
            //RpcIlHelper.EmitLdArg(ctorIL, 1);    // Load TRpcProxyArgs
            //RpcIlHelper.EmitLdArg(ctorIL, 2);    // Load proxyMethodsCreator

            //var baseCtor = typeof(TRpcProxyBase).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, proxyCtorArgs, null)
            //    ?? throw new NotImplementedException("Proxy ctor not found");
            //ctorIL.Emit(OpCodes.Call, baseCtor);
            //ctorIL.Emit(OpCodes.Ret);

            ////// Factory method
            ////var createMethodBuilder = typeBuilder.DefineMethod(RpcProxyBase<TMethodDef>.CreateMethodName, MethodAttributes.Public | MethodAttributes.Static, typeBuilder, proxyCtorArgs);
            ////var createIL = createMethodBuilder.GetILGenerator();

            ////EmitLdArg(createIL, 0);    // Load GrpcProxyArgs
            ////createIL.Emit(OpCodes.Newobj, ctorBuilder);
            ////createIL.Emit(OpCodes.Ret);

            //return (typeBuilder, staticCtorIL);
        }

        private static bool IsFaultAttribute(AttributeData attrbibute)
            => attrbibute.AttributeClass != null
            && (RpcBuilderUtil.IsClass(attrbibute.AttributeClass, "SciTech.Rpc.RpcFaultAttribute")
                || RpcBuilderUtil.IsClass(attrbibute.AttributeClass, "SciTech.Rpc.RpcFaultConverterAttribute"));

        private static IEnumerable<AttributeData> RetrieveServiceFaultAttributes(RpcServiceInfo serviceInfo)
            => RetrieveAttributes(serviceInfo.Type, serviceInfo.ServerType, IsFaultAttribute );

        private static IEnumerable<AttributeData> RetrieveAttributes(ISymbol clientMemberInfo, ISymbol? serverMemberInfo, Func<AttributeData, bool>? filter = null)//, Func<TAttribute,TAttribute,bool> equalityComparer)
        {
            // TODO: This is almost the same code as RetrieveServiceFaultAttributes, try to combine.
            List<AttributeData> faultAttributes;

            if (serverMemberInfo != null)
            {
                // If the server side definition is available, then the fault attributes of that definition
                // should be used.
                faultAttributes = serverMemberInfo.GetAttributes().ToList();

                // Validate that any fault attributes applied to the client side definition exists on the server side
                // definition
                foreach (var clientFaultAttribute in clientMemberInfo.GetAttributes())
                {
                    if (filter == null || filter(clientFaultAttribute))
                    {
                        throw new NotImplementedException();
                        //if (faultAttributes.Find(sa => sa.Match(clientFaultAttribute)/*equalityComparer(sa, clientFaultAttribute)*/) == null)
                        //{
                        //    throw new RpcDefinitionException($"Client side operation definition includes attribute '{clientFaultAttribute}' which is not applied on server side definition.");
                        //}
                    }
                }
            }
            else
            {
                if (filter != null)
                {
                    faultAttributes = clientMemberInfo.GetAttributes().Where(filter).ToList();
                }
                else
                {
                    faultAttributes = clientMemberInfo.GetAttributes().ToList();
                }
            }

            return faultAttributes;
        }


        private static string CreateFaultHandlerFromAttributes(string? baseHandler, IEnumerable<AttributeData> attributes)
        {
            //var factories = new List<IRpcFaultExceptionFactory>();
            //var converters = new List<IRpcClientExceptionConverter>();

            //foreach (var attribute in attributes)
            //{
            //    if (attribute is RpcFaultAttribute faultAttribute)
            //    {
            //        factories.Add(CreateFaultExceptionFactory(faultAttribute));
            //    }
            //    else if (attribute is RpcFaultConverterAttribute converterAttribute)
            //    {
            //        converters.Add(CreateClientExceptionConverter(converterAttribute));

            //        factories.Add(CreateFaultExceptionFactory(converterAttribute.FaultCode!, null)); // converterAttribute.FaultType)); ;
            //    }
            //}

            //if (factories.Count > 0 || converters.Count > 0)
            //{
            //    return new RpcClientFaultHandler(baseHandler, factories, converters);
            //}

            return "SciTech.Rpc.Client.RpcClientFaultHandler.Empty";
        }



        /// <summary>
        /// Adds all declared members of the service interface specified by <paramref name="serviceInfo"/> to the proxy type
        /// being built.
        /// </summary>
        private void AddServiceProxyMembers(RpcServiceInfo serviceInfo)
        {
            IEnumerable<AttributeData> faultAttributes = RetrieveServiceFaultAttributes(serviceInfo);

            string serviceFaultHandler = CreateFaultHandlerFromAttributes(null, faultAttributes);

            List<RpcMemberInfo>? serverSideMembers = null;
            if (serviceInfo.ServerType != null)
            {
                var serverServiceInfo = RpcBuilderUtil.TryGetServiceInfoFromType(serviceInfo.ServerType);
                if (serverServiceInfo == null)
                {
                    throw new RpcDefinitionException($"Server side type '{serviceInfo.ServerType}' is not an RpcService.");
                }

                serverSideMembers = this.builder.EnumOperationHandlers(serverServiceInfo, true).ToList();
            }

            foreach (var memberInfo in this.builder.EnumOperationHandlers(serviceInfo, false))
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
                else if (memberInfo is RpcPropertyInfo rpcPropertyInfo)
                {
                    this.CreatePropertyImpl(rpcPropertyInfo, serviceFaultHandler, serverSideMemberInfo);
                }
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

        private void CreatePropertyGetMethod(RpcPropertyInfo rpcPropertyInfo, string serviceFaultHandler, RpcMemberInfo? serverSideMemberInfo)
        {
            if (rpcPropertyInfo.GetOp != null)
            {
                this.AppendLine(@"get");
                this.CreateBlockingMethodBody(rpcPropertyInfo.GetOp, serviceFaultHandler, (serverSideMemberInfo as RpcPropertyInfo)?.GetOp);
            }
        }

        private void CreatePropertyImpl(RpcPropertyInfo propertyInfo, string serviceFaultHandler, RpcMemberInfo? serverSidePropertyInfo)
        {
            this.AppendLine();
            this.AppendLine($"{propertyInfo.GetOp.ReturnType.ToDisplayString()} {propertyInfo.Service.Type.ToDisplayString()}.{propertyInfo.Name}");
            this.AppendLine("{");
            this.IncreaseIndent();

            //FieldInfo eventDataField = this.CreateEventDataField(eventInfo);
            this.CreatePropertyGetMethod(propertyInfo, serviceFaultHandler, serverSidePropertyInfo);
            this.CreatePropertySetMethod(propertyInfo, serviceFaultHandler, serverSidePropertyInfo);
            //this.CreateEventRemoveHandler(eventInfo, eventDataField);
            this.DecreaseIndent();
            this.AppendLine("}");
        }

        private void CreatePropertySetMethod(RpcPropertyInfo rpcPropertyInfo, string serviceFaultHandler, RpcMemberInfo? serverSideMemberInfo)
        {
            if (rpcPropertyInfo.SetOp != null)
            {
                this.IncreaseIndent();
                this.AppendLine(@"set");

                this.CreateBlockingMethodBody(rpcPropertyInfo.SetOp, serviceFaultHandler, (serverSideMemberInfo as RpcPropertyInfo)?.SetOp);

                this.DecreaseIndent();
            }

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
            //int eventMethodIndex = this.CreateEventMethod(eventInfo);

            //this.CreateEventAddHandler(eventInfo, eventMethodIndex);
            //this.CreateEventRemoveHandler(eventInfo, eventMethodIndex);
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
        private void CreateServerStreamingMethodImpl(RpcOperationInfo operationInfo, string serviceFaultHandler, RpcMemberInfo? serverSideMemberInfo)
        {
            //if (this.typeBuilder == null)
            //{
            //    throw new InvalidOperationException();
            //}

            //if (operationInfo.CallbackParameterIndex != null)
            //{
            //    CreateCallbackMethodImpl(operationInfo, serviceFaultHandler, serverSideMemberInfo);
            //    return;
            //}

            //var implMethodBuilder = this.typeBuilder.DefineMethod($"{operationInfo.Service.Name}.{operationInfo.Method.Name}", MethodAttributes.Private | MethodAttributes.Virtual,
            //    returnType: operationInfo.Method.ReturnType,
            //    parameterTypes: operationInfo.Method.GetParameters().Select(p => p.ParameterType).ToArray());

            //this.typeBuilder.DefineMethodOverride(implMethodBuilder, operationInfo.Method);

            //var il = implMethodBuilder.GetILGenerator();

            //var objectIdField = GetProxyField(RpcProxyBase<TMethodDef>.ObjectIdFieldName);
            //var proxyMethodsField = GetProxyField(RpcProxyBase<TMethodDef>.ProxyMethodsFieldName);
            //int methodDefIndex = this.CreateMethodDefinitionField(operationInfo, serviceFaultHandler, serverSideMemberInfo);

            //il.Emit(OpCodes.Ldarg_0);// Load this
            //il.Emit(OpCodes.Ldarg_0);// Load this (for proxyMethods field )
            //il.Emit(OpCodes.Ldfld, proxyMethodsField); //Load method def field
            //il.Emit(OpCodes.Ldc_I4, methodDefIndex);
            //il.Emit(OpCodes.Ldelem, typeof(TMethodDef)); // load method def (this.proxyMethods[methodDefIndex])

            //bool isSingleton = operationInfo.Service.IsSingleton;

            //Type[] reqestTypeCtorArgs = new Type[operationInfo.RequestParameters.Length + (isSingleton ? 0 : 1)];
            //int argIndex = 0;

            //if (!isSingleton)
            //{
            //    il.Emit(OpCodes.Ldarg_0);// Load this (for objectId field)
            //    il.Emit(OpCodes.Ldfld, objectIdField); //Load objectId field
            //    reqestTypeCtorArgs[argIndex++] = typeof(RpcObjectId);
            //}

            //// Load parameters
            //foreach (var requestParameter in operationInfo.RequestParameters)
            //{
            //    RpcIlHelper.EmitLdArg(il, requestParameter.Index + 1);
            //    reqestTypeCtorArgs[argIndex++] = requestParameter.Type;
            //}

            //var ctorInfo = operationInfo.RequestType.GetConstructor(reqestTypeCtorArgs)
            //    ?? throw new NotImplementedException($"Request type constructor not found");
            //il.Emit(OpCodes.Newobj, ctorInfo);  // new RpcRequestType<>( objectId, ...)

            //MethodInfo callUnaryMethodInfo;
            //string callerMethodName = RpcProxyBase<TMethodDef>.CallAsyncEnumerableMethodName;
            //var callUnaryMethodDefInfo = GetProxyMethod(callerMethodName);
            //callUnaryMethodInfo = callUnaryMethodDefInfo.MakeGenericMethod(operationInfo.RequestType, operationInfo.ResponseReturnType, operationInfo.ReturnType);

            //EmitResponseConverter(il, operationInfo);

            //if (operationInfo.CancellationTokenIndex != null)
            //{
            //    RpcIlHelper.EmitLdArg(il, operationInfo.CancellationTokenIndex.Value + 1);
            //}
            //else
            //{
            //    // Load CancellationToken.None
            //    var ctLocal = il.DeclareLocal(typeof(CancellationToken));
            //    il.Emit(OpCodes.Ldloca_S, ctLocal);
            //    il.Emit(OpCodes.Initobj, typeof(CancellationToken));
            //    Debug.Assert(ctLocal.LocalIndex == 0);
            //    il.Emit(OpCodes.Ldloc_0);//, ctLocal.LocalIndex);
            //}

            //il.Emit(OpCodes.Call, callUnaryMethodInfo);
            //il.Emit(OpCodes.Ret); //return
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
            string serviceFaultHandler,
            RpcMemberInfo? serverSideMethodInfo)
        {
            //if (this.typeBuilder == null)
            //{
            //    throw new InvalidOperationException();
            //}

            //var implMethodBuilder = this.typeBuilder.DefineMethod(
            //    $"{operationInfo.Service.Name}.{operationInfo.Method.Name}",
            //    MethodAttributes.Private | MethodAttributes.Virtual,
            //    returnType: operationInfo.Method.ReturnType,
            //    parameterTypes: operationInfo.Method.GetParameters().Select(p => p.ParameterType).ToArray());

            //this.typeBuilder.DefineMethodOverride(implMethodBuilder, operationInfo.Method);

            //var il = implMethodBuilder.GetILGenerator();

            //var objectIdField = GetProxyField(RpcProxyBase<TMethodDef>.ObjectIdFieldName);
            //var proxyMethodsField = GetProxyField(RpcProxyBase<TMethodDef>.ProxyMethodsFieldName);
            //il.Emit(OpCodes.Ldarg_0);// Load this

            //int methodDefIndex = this.CreateMethodDefinitionField(operationInfo, serviceFaultHandler, serverSideMethodInfo);

            //il.Emit(OpCodes.Ldarg_0);// Load this (for proxyMethods field )
            //il.Emit(OpCodes.Ldfld, proxyMethodsField); //Load method def field
            //il.Emit(OpCodes.Ldc_I4, methodDefIndex);
            //il.Emit(OpCodes.Ldelem, typeof(TMethodDef)); // load method def (this.proxyMethods[methodDefIndex])

            //bool isSingleton = operationInfo.Service.IsSingleton;

            //Type[] reqestTypeCtorArgs = new Type[operationInfo.RequestParameters.Length + (isSingleton ? 0 : 1)];
            //int argIndex = 0;

            //if (!isSingleton)
            //{
            //    il.Emit(OpCodes.Ldarg_0);// Load this (for objectId field)
            //    il.Emit(OpCodes.Ldfld, objectIdField); //Load objectId field
            //    reqestTypeCtorArgs[argIndex++] = typeof(RpcObjectId);
            //}

            //// Load parameters
            //foreach (var requestParameter in operationInfo.RequestParameters)
            //{
            //    RpcIlHelper.EmitLdArg(il, requestParameter.Index + 1);
            //    reqestTypeCtorArgs[argIndex++] = requestParameter.Type;
            //}

            //var ctorInfo = operationInfo.RequestType.GetConstructor(reqestTypeCtorArgs)
            //    ?? throw new NotImplementedException($"Request type constructor not found");
            //il.Emit(OpCodes.Newobj, ctorInfo);  // new RpcRequestType<>( objectId, ...)

            //MethodInfo callUnaryMethodInfo;

            //if (operationInfo.ReturnType != typeof(void))
            //{
            //    string callerMethodName = RpcProxyBase<TMethodDef>.CallUnaryMethodAsyncName;

            //    var callUnaryMethodDefInfo = GetProxyMethod(callerMethodName);
            //    callUnaryMethodInfo = callUnaryMethodDefInfo.MakeGenericMethod(operationInfo.RequestType, operationInfo.ResponseReturnType, operationInfo.ReturnType);

            //    EmitResponseConverter(il, operationInfo);
            //}
            //else
            //{
            //    string callerMethodName = RpcProxyBase<TMethodDef>.CallUnaryVoidMethodAsyncName;

            //    var callUnaryMethodDefInfo = GetProxyMethod(callerMethodName);
            //    callUnaryMethodInfo = callUnaryMethodDefInfo.MakeGenericMethod(operationInfo.RequestType);
            //}

            //if (operationInfo.CancellationTokenIndex != null)
            //{
            //    RpcIlHelper.EmitLdArg(il, operationInfo.CancellationTokenIndex.Value + 1);
            //}
            //else
            //{
            //    // Load CancellationToken.None
            //    var ctLocal = il.DeclareLocal(typeof(CancellationToken));
            //    il.Emit(OpCodes.Ldloca_S, ctLocal);
            //    il.Emit(OpCodes.Initobj, typeof(CancellationToken));
            //    Debug.Assert(ctLocal.LocalIndex == 0);
            //    il.Emit(OpCodes.Ldloc_0);//, ctLocal.LocalIndex);
            //}

            //il.Emit(OpCodes.Call, callUnaryMethodInfo);
            //il.Emit(OpCodes.Ret); //return
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
        private void CreateBlockingMethodImpl(RpcOperationInfo operationInfo, string serviceFaultHandler, RpcMemberInfo? serverSideMemberInfo)
        {
            if (this.typeBuilder == null)
            {
                throw new InvalidOperationException();
            }

            typeBuilder.Append($@"        {operationInfo.Method.ReturnType.ToDisplayString()} {operationInfo.Service.Type.ToDisplayString()}.{operationInfo.Method.Name}(");
            bool firstParam = true;
            foreach (var param in operationInfo.Method.Parameters)
            {
                if (!firstParam)
                {
                    typeBuilder.Append(", ");
                }

                typeBuilder.Append(Invariant($"{param.Type.ToDisplayString()} {param.Name}"));
                firstParam = false;
            }
            typeBuilder.Append(@")
");

            CreateBlockingMethodBody(operationInfo, serviceFaultHandler, serverSideMemberInfo);




            //var implMethodBuilder = this.typeBuilder.DefineMethod($"{operationInfo.Service.Name}.{operationInfo.Method.Name}", MethodAttributes.Private | MethodAttributes.Virtual,
            //    returnType: operationInfo.Method.ReturnType,
            //    parameterTypes: operationInfo.Method.GetParameters().Select(p => p.ParameterType).ToArray());

            //this.typeBuilder.DefineMethodOverride(implMethodBuilder, operationInfo.Method);

            //var il = implMethodBuilder.GetILGenerator();

            //var objectIdField = GetProxyField(RpcProxyBase<TMethodDef>.ObjectIdFieldName);
            //var proxyMethodsField = GetProxyField(RpcProxyBase<TMethodDef>.ProxyMethodsFieldName);

            //il.Emit(OpCodes.Ldarg_0);// Load this
            //il.Emit(OpCodes.Ldarg_0);// Load this (for proxyMethods field )
            //il.Emit(OpCodes.Ldfld, proxyMethodsField); //Load method def field
            //il.Emit(OpCodes.Ldc_I4, methodDefIndex);
            //il.Emit(OpCodes.Ldelem, typeof(TMethodDef)); // load method def (this.proxyMethods[methodDefIndex])

            //bool isSingleton = operationInfo.Service.IsSingleton;

            //Type[] reqestTypeCtorArgs = new Type[operationInfo.RequestParameters.Length + (isSingleton ? 0 : 1)];
            //int argIndex = 0;

            //if (!isSingleton)
            //{
            //    il.Emit(OpCodes.Ldarg_0);// Load this (for objectId field)
            //    il.Emit(OpCodes.Ldfld, objectIdField); //Load objectId field
            //    reqestTypeCtorArgs[argIndex++] = typeof(RpcObjectId);
            //}

            //// Load parameters
            //foreach (var requestParameter in operationInfo.RequestParameters)
            //{
            //    RpcIlHelper.EmitLdArg(il, requestParameter.Index + 1);
            //    reqestTypeCtorArgs[argIndex++] = requestParameter.Type;
            //}

            //var ctorInfo = operationInfo.RequestType.GetConstructor(reqestTypeCtorArgs)
            //    ?? throw new NotImplementedException($"Request type constructor not found");
            //il.Emit(OpCodes.Newobj, ctorInfo);  // new RpcRequestType<>( objectId, ...)

            //MethodInfo callUnaryMethodInfo;
            //if (operationInfo.ReturnType != typeof(void))
            //{
            //    string callerMethodName = RpcProxyBase<TMethodDef>.CallUnaryMethodName;

            //    var callUnaryMethodDefInfo = GetProxyMethod(callerMethodName);
            //    callUnaryMethodInfo = callUnaryMethodDefInfo.MakeGenericMethod(operationInfo.RequestType, operationInfo.ResponseReturnType, operationInfo.ReturnType);

            //    EmitResponseConverter(il, operationInfo);
            //}
            //else
            //{
            //    string callerMethodName = RpcProxyBase<TMethodDef>.CallUnaryVoidMethodName;
            //    var callUnaryMethodDefInfo = GetProxyMethod(callerMethodName);
            //    callUnaryMethodInfo = callUnaryMethodDefInfo.MakeGenericMethod(operationInfo.RequestType);
            //}

            //if (operationInfo.CancellationTokenIndex != null)
            //{
            //    RpcIlHelper.EmitLdArg(il, operationInfo.CancellationTokenIndex.Value + 1);
            //}
            //else
            //{
            //    // Load CancellationToken.None
            //    var ctLocal = il.DeclareLocal(typeof(CancellationToken));
            //    il.Emit(OpCodes.Ldloca_S, ctLocal);
            //    il.Emit(OpCodes.Initobj, typeof(CancellationToken));
            //    Debug.Assert(ctLocal.LocalIndex == 0);
            //    il.Emit(OpCodes.Ldloc_0);//, ctLocal.LocalIndex);
            //}

            //il.Emit(OpCodes.Call, callUnaryMethodInfo);
            //il.Emit(OpCodes.Ret); //return
        }



        private void AppendLine(FormattableString line)
        {
            if (isStartOfLine)
            {
                this.typeBuilder.Append(this.Indent);
            }

            this.typeBuilder.AppendLine(Invariant(line));
            isStartOfLine = true;


        }

        private bool isStartOfLine;

        private void AppendLine(string line="")
        {
            if( isStartOfLine )
            {
                this.typeBuilder.Append(this.Indent);
            }

            this.typeBuilder.AppendLine(line);
            isStartOfLine = true;
        }

        private void Append(FormattableString line)
        {
            if (isStartOfLine)
            {
                this.typeBuilder.Append(this.Indent);                
            }

            this.typeBuilder.Append(Invariant(line));
            isStartOfLine = false;


        }

        private void Append(string line)
        {
            if (isStartOfLine)
            {
                this.typeBuilder.Append(this.Indent);
            }

            this.typeBuilder.Append(line);
            isStartOfLine = false;
        }


        private string Indent { get; set; }

        private void IncreaseIndent()
        {
            this.Indent += "    ";
        }

        private void DecreaseIndent()
        {
            this.Indent = this.Indent.Substring(4);
        }

        private void CreateBlockingMethodBody(RpcOperationInfo operationInfo, string serviceFaultHandler, RpcMemberInfo? serverSideMemberInfo)
        {
            bool firstParam;
            int methodDefIndex = this.CreateMethodDefinitionField(operationInfo, serviceFaultHandler, serverSideMemberInfo);
            if (operationInfo.ReturnType.SpecialType != SpecialType.System_Void)
            {
                this.AppendLine("{");
                this.IncreaseIndent();
                this.AppendLine($"return this.CallUnaryMethod<{operationInfo.RequestType}, {operationInfo.ResponseType}, {operationInfo.ReturnType.ToDisplayString()}>(");
            }
            else
            {
                this.AppendLine("{");
                this.IncreaseIndent();
                this.AppendLine($"return this.CallUnaryVoidMethod<{operationInfo.RequestType}>(" );
            }
            this.IncreaseIndent();
            this.AppendLine($"this.proxyMethods[{methodDefIndex}]");
            this.Append($"new {operationInfo.RequestType}(");

            bool isSingleton = operationInfo.Service.IsSingleton;
            firstParam = true;
            if (!isSingleton)
            {
                this.Append("this.objectId");
                firstParam = false;
            }

            foreach (var param in operationInfo.RequestParameters)
            {
                if (!firstParam)
                {
                    this.Append(", ");
                }

                this.Append($"{param.Type.ToDisplayString()} {param.Index}");
                firstParam = false;
            }

            this.AppendLine("),");
            this.AppendLine($"{serviceFaultHandler}");
            this.AppendLine($"{operationInfo.CancellationTokenName}");
            this.DecreaseIndent();
            this.AppendLine(");");
            this.DecreaseIndent();
            this.AppendLine("}");
        }

        private readonly Dictionary<string, MethodDefIndex> methodDefinitionIndices = new Dictionary<string, MethodDefIndex>();
        private class MethodDefIndex
        {
            internal readonly int Index;

            internal readonly string RequestType;

            internal readonly string ResponseType;

            internal MethodDefIndex(int index, string requestType, string responseType)
            {
                this.Index = index;
                this.RequestType = requestType;
                this.ResponseType = responseType;
            }
        }

        private int CreateMethodDefinitionField(RpcOperationInfo operationInfo,
            string serviceFaultHandler,
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

            string faultHandler = RetrieveClientFaultHandler(operationInfo, serviceFaultHandler, serverSideMemberInfo);

            int methodDefIndex = this.AddMethodDef(
                operationInfo,
                RpcMethodType.Unary,
                null,
                faultHandler);

            this.methodDefinitionIndices.Add(operationName, new MethodDefIndex(methodDefIndex, operationInfo.RequestType, operationInfo.ResponseType));
            return methodDefIndex;
        }

        private static string RetrieveClientFaultHandler(RpcOperationInfo operationInfo,
            string serviceFaultHandler,
            RpcMemberInfo? serverSideMemberInfo)
        {
            IEnumerable<AttributeData> faultAttributes = RetrieveFaultAttributes(operationInfo, serverSideMemberInfo);

            return CreateFaultHandlerFromAttributes(serviceFaultHandler, faultAttributes);
        }

        private static IEnumerable<AttributeData> RetrieveFaultAttributes(RpcOperationInfo operationInfo, RpcMemberInfo? serverSideMemberInfo)
            => RetrieveAttributes(operationInfo.DeclaringMember, serverSideMemberInfo?.DeclaringMember, IsFaultAttribute);

        private int AddMethodDef(
            RpcOperationInfo memberInfo,
            RpcMethodType methodType,
            string? serializer,
            string faultHandler)
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

        private List<string>? createMethodDefExpressions = new List<string>();


        private int AddMethodDef(
            RpcMemberInfo memberInfo,
            string methodName,
            RpcMethodType methodType,
            string requestType,
            string responseType,
            string? serializer,
            string faultHandler)
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
        private static string CreateMethodDefExpression(
            RpcMemberInfo memberInfo,
            string methodName,
            RpcMethodType methodType,
            string requestType,
            string responseType,
            string? serializer,
            string faultHandler)
        {
            return Invariant(
                $"new LightweightMethodDef<{requestType}, {responseType}>(RpcMethodType.{methodType}, \"{memberInfo.FullName}\", {serializer ?? "null"}, {faultHandler})");

            //new LightweightMethodDef<RpcObjectRequest<int, int>, RpcResponse<int>>(RpcMethodType.Unary, "SciTech.Rpc.CodeGen.SimpleService.Add", null, null),
            //var createMethodDefMethodDef = GetProxyMethod(RpcProxyBase<TMethodDef>.CreateMethodDefName, BindingFlags.Static | BindingFlags.Public);
            //var createMethodDefMethod = createMethodDefMethodDef.MakeGenericMethod(requestType, responseType);
            //return (TMethodDef?)createMethodDefMethod.Invoke(null, new object?[] { methodType, memberInfo.Service.FullName, methodName, serializer, faultHandler })
            //    ?? throw new InvalidOperationException("Incorrect CreateMethodDef implementatin");
        }
        // return new LightweightMethodDef<TRequest, TResponse>(methodType, $"{serviceName}.{methodName}", serializer, faultHandler);
    }

    internal class RpcTypeBuilder
    {
        StringBuilder typeBuilder;

        public RpcTypeBuilder()
        {

        }
    }
}
