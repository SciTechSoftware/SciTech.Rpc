using Microsoft.CodeAnalysis;
using SciTech.Rpc.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using static System.FormattableString;

namespace SciTech.Rpc.CodeGen.Client
{
    internal class RpcServiceProxyBuilder
    {        
        private GeneratorExecutionContext generatorContext;

        private IReadOnlyList<RpcServiceInfo> allServices;
        private RpcBuilderUtil builder;
        private SourceBuilder typeBuilder;

        internal RpcServiceProxyBuilder(
            IReadOnlyList<RpcServiceInfo> allServices,
            GeneratorExecutionContext generatorContext,
            //ModuleBuilder moduleBuilder, 
            Dictionary<string, string> methodDefExpressions)
        {
            this.allServices = allServices;
            this.generatorContext = generatorContext;
            this.createMethodDefExpressions = methodDefExpressions;


        }

        protected string ProxyArgsName => "SciTech.Rpc.Lightweight.Client.Internal.LightweightProxyArgs";
        protected string ProxyBaseName => "SciTech.Rpc.Lightweight.Client.Internal.LightweightProxyBase";

        internal GeneratedProxyType BuildProxy()
        {
            var declaredService = allServices.Single(s => s.IsDeclaredService);
            this.builder = new RpcBuilderUtil(this.generatorContext.Compilation);

            var typeBuilder = CreateTypeBuilder(declaredService);
            foreach (var service in allServices)
            {
                AddServiceProxyMembers(service);
            }

            typeBuilder.DecreaseIndent();
            typeBuilder.AppendLine("}");

            SourceBuilder typeDictionaryBuilder = new();
            string proxyTypeName = GetProxyTypeName(declaredService);

            if (typeDictionaryBuilder.Length > 0)
            {
                typeDictionaryBuilder.AppendLine(",");
            }
            typeDictionaryBuilder.AppendLine("{");
            typeDictionaryBuilder.IncreaseIndent();
            typeDictionaryBuilder.AppendLine($"typeof({declaredService.Type.ToString() ?? ""}),");
            typeDictionaryBuilder.AppendLine($"new System.Func<{ProxyArgsName}, object>(");
            typeDictionaryBuilder.AppendLine($"    args => new {proxyTypeName}(args))");
            typeDictionaryBuilder.DecreaseIndent();
            typeDictionaryBuilder.Append("}");

            return new GeneratedProxyType(proxyTypeName, this.typeBuilder.ToString(), typeDictionaryBuilder.ToString());
        }

        private static string GetFlatName(ITypeSymbol type) => GetFlatName(type.ToString());

        private static string GetFlatName([AllowNull]string name)
        {
            name ??= "";

            return name.Replace('.', '_');
        }
        private SourceBuilder CreateTypeBuilder(RpcServiceInfo serviceInfo)
        {
            string proxyTypeName = GetProxyTypeName(serviceInfo);
            var typeBuilder = this.typeBuilder = new SourceBuilder();// this.moduleBuilder.DefineType(proxyTypeName, TypeAttributes.Public | TypeAttributes.BeforeFieldInit, typeof(TRpcProxyBase));

            typeBuilder.Append($"internal class {proxyTypeName} : {ProxyBaseName}, ");

            bool firstService = true;

            foreach (var implementedService in this.allServices)
            {
                if (!firstService)
                {
                    typeBuilder.Append(", ");
                }

                typeBuilder.Append(implementedService.Type.ToDisplayString());
                firstService = false;
            }

            typeBuilder.AppendLine();
            typeBuilder.AppendLine("{");
            typeBuilder.IncreaseIndent();
            typeBuilder.AppendLine($"public {proxyTypeName}({this.ProxyArgsName} proxyArgs) : base(proxyArgs)");
            typeBuilder.AppendLine("{");
            typeBuilder.AppendLine("}");

            return this.typeBuilder;

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

        private static string GetProxyTypeName(RpcServiceInfo serviceInfo)
        {
            return $"__{GetFlatName(serviceInfo.FullName)}_Proxy";
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
                if (serverServiceInfo != null)
                {
                    serverSideMembers = this.builder.EnumOperationHandlers(serverServiceInfo, true).ToList();
                }
                else 
                {
                    this.generatorContext.ReportDiagnostic(
                        Diagnostic.Create(RpcDiagnostics.ServerSideTypeIsNotRpcServiceRule, serviceInfo.Type.Locations.FirstOrDefault(), serviceInfo.Type.ToString()));
                }                
            }

            foreach (var memberInfo in this.builder.EnumOperationHandlers(serviceInfo, false))
            {
                RpcMemberInfo? serverSideMemberInfo = null;
                if (serverSideMembers != null)
                {
                    serverSideMemberInfo = serverSideMembers.Find(sm => memberInfo.FullName == sm.FullName);

                    if (serverSideMemberInfo == null)
                    {
                        this.generatorContext.ReportDiagnostic(
                            Diagnostic.Create(RpcDiagnostics.NoMatchingServerSideMemberRule, serviceInfo.Type.Locations.FirstOrDefault(), 
                            memberInfo.FullName, serviceInfo.Type.ToString()));
                        continue;
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
            var builder = this.typeBuilder;
            if (rpcPropertyInfo.GetOp != null)
            {
                builder.AppendLine(@"get");
                this.CreateBlockingMethodBody(rpcPropertyInfo.GetOp, serviceFaultHandler, (serverSideMemberInfo as RpcPropertyInfo)?.GetOp);
            }
        }

        private void CreatePropertyImpl(RpcPropertyInfo propertyInfo, string serviceFaultHandler, RpcMemberInfo? serverSidePropertyInfo)
        {
            if (propertyInfo.GetOp?.ReturnType == null || propertyInfo.GetOp.ReturnType.SpecialType == SpecialType.System_Void)
            {
                throw new InvalidOperationException("Property must have non-void return type");
            }
            
            var builder = this.typeBuilder;
            builder.AppendLine();
            builder.AppendLine($"{propertyInfo.GetOp.ReturnType.ToDisplayString()} {propertyInfo.Service.Type.ToDisplayString()}.{propertyInfo.Name}");
            builder.AppendLine("{");
            builder.IncreaseIndent();

            //FieldInfo eventDataField = this.CreateEventDataField(eventInfo);
            this.CreatePropertyGetMethod(propertyInfo, serviceFaultHandler, serverSidePropertyInfo);
            this.CreatePropertySetMethod(propertyInfo, serviceFaultHandler, serverSidePropertyInfo);
            //this.CreateEventRemoveHandler(eventInfo, eventDataField);
            builder.DecreaseIndent();
            builder.AppendLine("}");
        }

        private void CreatePropertySetMethod(RpcPropertyInfo rpcPropertyInfo, string serviceFaultHandler, RpcMemberInfo? serverSideMemberInfo)
        {
            var builder = this.typeBuilder;
            if (rpcPropertyInfo.SetOp != null)
            {
                builder.IncreaseIndent();
                builder.AppendLine(@"set");

                this.CreateBlockingMethodBody(rpcPropertyInfo.SetOp, serviceFaultHandler, (serverSideMemberInfo as RpcPropertyInfo)?.SetOp);

                builder.DecreaseIndent();
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
            if (this.typeBuilder == null)
            {
                throw new InvalidOperationException();
            }
            typeBuilder.Append($@"{operationInfo.Method.ReturnType.ToDisplayString()} {operationInfo.Service.Type.ToDisplayString()}.{operationInfo.Method.Name}(");
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
            typeBuilder.AppendLine(")");

            this.CreateAsyncMethodBody(operationInfo, serviceFaultHandler, serverSideMethodInfo);

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

            typeBuilder.Append($@"{operationInfo.Method.ReturnType.ToDisplayString()} {operationInfo.Service.Type.ToDisplayString()}.{operationInfo.Method.Name}(");
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
            typeBuilder.AppendLine(")");

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


        protected string MethodDefsTypeName => "__SciTech_Rpc_Lightweight_GeneratedMethodDefs";

        private void CreateBlockingMethodBody(RpcOperationInfo operationInfo, string serviceFaultHandler, RpcMemberInfo? serverSideMemberInfo)
        {
            var builder = this.typeBuilder;
            bool firstParam;
            string methodDefIndex = this.CreateMethodDefinitionField(operationInfo, serviceFaultHandler, serverSideMemberInfo);
            if (operationInfo.ReturnType != null && operationInfo.ReturnType.SpecialType != SpecialType.System_Void)
            {
                builder.AppendLine("{");
                builder.IncreaseIndent();
                builder.AppendLine($"return this.CallUnaryMethod<{operationInfo.RequestType}, {operationInfo.ResponseType}, {operationInfo.ReturnType.ToDisplayString()}>(");
            }
            else
            {
                builder.AppendLine("{");
                builder.IncreaseIndent();
                builder.AppendLine($"return this.CallUnaryVoidMethod<{operationInfo.RequestType}>(" );
            }
            builder.IncreaseIndent();
            builder.AppendLine($"{this.MethodDefsTypeName}.{methodDefIndex},");
            builder.Append($"new {operationInfo.RequestType}(");

            bool isSingleton = operationInfo.Service.IsSingleton;
            firstParam = true;
            if (!isSingleton)
            {
                builder.Append("this.objectId");
                firstParam = false;
            }

            foreach (var param in operationInfo.RequestParameters)
            {
                if (!firstParam)
                {
                    builder.Append(", ");
                }

                builder.Append($"{param.Type.ToDisplayString()} {param.Index}");
                firstParam = false;
            }

            builder.AppendLine("),");
            builder.AppendLine($"{serviceFaultHandler},");
            builder.AppendLine($"{operationInfo.CancellationTokenName}");
            builder.DecreaseIndent();
            builder.AppendLine(");");
            builder.DecreaseIndent();
            builder.AppendLine("}");
        }


        private void CreateAsyncMethodBody(RpcOperationInfo operationInfo, string serviceFaultHandler, RpcMemberInfo? serverSideMemberInfo)
        {
            var builder = this.typeBuilder;
            bool firstParam;
            string methodDefIndex = this.CreateMethodDefinitionField(operationInfo, serviceFaultHandler, serverSideMemberInfo);
            if (operationInfo.ReturnType != null && operationInfo.ReturnType.SpecialType != SpecialType.System_Void)
            {
                builder.AppendLine("{");
                builder.IncreaseIndent();
                builder.AppendLine($"return this.CallUnaryMethodAsync<{operationInfo.RequestType}, {operationInfo.ResponseType}, {operationInfo.ReturnType.ToDisplayString()}>(");
            }
            else
            {
                builder.AppendLine("{");
                builder.IncreaseIndent();
                builder.AppendLine($"return this.CallUnaryVoidMethodAsync<{operationInfo.RequestType}>(");
            }
            builder.IncreaseIndent();
            builder.AppendLine($"{this.MethodDefsTypeName}.{methodDefIndex},");
            builder.Append($"new {operationInfo.RequestType}(");

            bool isSingleton = operationInfo.Service.IsSingleton;
            firstParam = true;
            if (!isSingleton)
            {
                builder.Append("this.objectId");
                firstParam = false;
            }

            foreach (var param in operationInfo.RequestParameters)
            {
                if (!firstParam)
                {
                    builder.Append(", ");
                }

                builder.Append($"{param.Type.ToDisplayString()} {param.Index}");
                firstParam = false;
            }

            builder.AppendLine("),");
            builder.AppendLine($"{serviceFaultHandler},");
            builder.AppendLine($"{operationInfo.CancellationTokenName}");
            builder.DecreaseIndent();
            builder.AppendLine(");");
            builder.DecreaseIndent();
            builder.AppendLine("}");
        }

        //private readonly Dictionary<string, MethodDefIndex> methodDefinitionIndices = new Dictionary<string, MethodDefIndex>();
        //private const string MethodDefIndent = "        ";
        //private const string TypeDictionaryEntryIndent = "            ";


        private class MethodDefIndex
        {
            internal readonly string FieldName;

            internal readonly string RequestType;

            internal readonly string ResponseType;

            internal MethodDefIndex(string fieldName, string requestType, string responseType)
            {
                this.FieldName = fieldName;
                this.RequestType = requestType;
                this.ResponseType = responseType;
            }
        }

        private string CreateMethodDefinitionField(RpcOperationInfo operationInfo,
            string serviceFaultHandler,
            RpcMemberInfo? serverSideMemberInfo)
        {
            if (this.typeBuilder == null)
            {
                throw new InvalidOperationException();
            }

            string operationName = operationInfo.FullName;

            //if (this.methodDefinitionIndices.TryGetValue(operationName, out var methodDefField))
            //{
            //    if (!Equals(methodDefField.RequestType, operationInfo.RequestType)
            //        || !Equals(methodDefField.ResponseType, operationInfo.ResponseType))
            //    {
            //        throw new RpcDefinitionException("Different versions of an RPC operation must have the same request and response types.");
            //    }

            //    return methodDefField.Index;
            //}

            string faultHandler = RetrieveClientFaultHandler(operationInfo, serviceFaultHandler, serverSideMemberInfo);

            string methodDefIndex = this.AddMethodDef(
                operationInfo,
                RpcMethodType.Unary,
                null,
                faultHandler);

            //this.methodDefinitionIndices.Add(operationName, new MethodDefIndex(methodDefIndex, operationInfo.RequestType, operationInfo.ResponseType));
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

        private string AddMethodDef(
            RpcOperationInfo memberInfo,
            RpcMethodType methodType,
            string? serializer,
            string faultHandler)
        {
            var methodDefExpression = CreateMethodDefExpression(memberInfo, memberInfo.Name, methodType, memberInfo.RequestType, memberInfo.ResponseType,
                serializer, faultHandler);

            string operationName = GetFlatName( memberInfo.FullName );
            if( this.createMethodDefExpressions.TryGetValue(operationName, out var currMethodDefExpression ))
            {
                if( methodDefExpression != currMethodDefExpression)
                {
                    throw new NotImplementedException("AddIssue");
                }
            } else
            {
                this.createMethodDefExpressions.Add(operationName, methodDefExpression);
            }

            return operationName; 
        }

        private Dictionary<string,string> createMethodDefExpressions = new Dictionary<string, string>();


        protected string MethodDefTypeName => "SciTech.Rpc.Lightweight.Client.Internal.LightweightMethodDef";

        
        private  string CreateMethodDefExpression(
            RpcMemberInfo memberInfo,
            string methodName,
            RpcMethodType methodType,
            string requestType,
            string responseType,
            string? serializer,
            string faultHandler)
        {
            return Invariant(
@$"new {this.MethodDefTypeName}<{requestType}, {responseType}>(
    RpcMethodType.{methodType}, 
    ""{memberInfo.FullName}"", 
    {serializer ?? "null"}, 
    {faultHandler})");

            //new LightweightMethodDef<RpcObjectRequest<int, int>, RpcResponse<int>>(RpcMethodType.Unary, "SciTech.Rpc.CodeGen.SimpleService.Add", null, null),
            //var createMethodDefMethodDef = GetProxyMethod(RpcProxyBase<TMethodDef>.CreateMethodDefName, BindingFlags.Static | BindingFlags.Public);
            //var createMethodDefMethod = createMethodDefMethodDef.MakeGenericMethod(requestType, responseType);
            //return (TMethodDef?)createMethodDefMethod.Invoke(null, new object?[] { methodType, memberInfo.Service.FullName, methodName, serializer, faultHandler })
            //    ?? throw new InvalidOperationException("Incorrect CreateMethodDef implementatin");
        }
        // return new LightweightMethodDef<TRequest, TResponse>(methodType, $"{serviceName}.{methodName}", serializer, faultHandler);
    }

    internal class SourceBuilder
    {
        private readonly StringBuilder builder = new StringBuilder();
        private readonly string lineTerminator;
        private readonly int IndentCount = 4;

        public SourceBuilder(string? lineTerminator=null)
        {
            this.lineTerminator = lineTerminator ?? Environment.NewLine;
        }

        private bool isStartOfLine=true;


        public void AppendLine(string line = "")
        {
            if (isStartOfLine)
            {
                this.builder.Append(this.Indent);
            }

            this.builder.Append(line);
            this.builder.Append(this.lineTerminator);
            isStartOfLine = true;
        }

        private const string LineTerminators = "\r\n";

        public void AppendBlock(ReadOnlySpan<char> text)
        {            
            var currText = text;
            while (currText.Length > 0)
            {
                if (this.isStartOfLine)
                {
                    this.builder.Append(this.Indent);
                }

                int lineEndIndex = currText.IndexOfAny('\r', '\n');
                ReadOnlySpan<char> line;
                if (lineEndIndex >= 0)
                {
                    line = currText[0..lineEndIndex];
                    if (lineEndIndex + 1 < currText.Length)
                    {
                        char currLineTerminator = currText[lineEndIndex];
                        char nextCh = currText[lineEndIndex + 1];
                        if ((nextCh == '\r' || nextCh == '\n') && nextCh != currLineTerminator)
                        {
                            lineEndIndex++;
                        }
                    }

                    currText = currText[(lineEndIndex + 1)..];
                    this.isStartOfLine = true;
                }
                else
                {
                    line = currText;
                    currText = ReadOnlySpan<char>.Empty;
                    this.isStartOfLine = false;
                }

                this.builder.Append(line);
                if (this.isStartOfLine) this.builder.Append(this.lineTerminator);
            }
        }

        public void Append(FormattableString line)
        {
            if (isStartOfLine)
            {
                this.builder.Append(this.Indent);
            }

            this.builder.Append(Invariant(line));
            this.isStartOfLine = false;
        }

        public void Append(string line)
        {
            if (isStartOfLine)
            {
                this.builder.Append(this.Indent);
            }

            this.builder.Append(line);
            isStartOfLine = false;
        }


        public string Indent { get; set; } = "";
        
        public int Length => this.builder.Length;

        public void IncreaseIndent()
        {
            this.Indent += "    ";
        }
        
        public void IncreaseIndent(int count)
        {
            this.Indent += new string(' ', count * IndentCount);
        }

        public void DecreaseIndent()
        {
            this.Indent = this.Indent.Substring(IndentCount);
        }

        public void DecreaseIndent(int count)
        {
            this.Indent = this.Indent.Substring(IndentCount * count);
        }

        public override string ToString()
        {
            return this.builder.ToString();
        }

    }

    public static class RpcDiagnostics
    {
        public const string RpcDuplicateOperationRuleId = "RPC1001";

        public const string RpcMissingServerOperationRuleId = "RPC1002";

        public const string RpcOperationMbrArgumentId = "RPC1003";

        public const string RpcOperationMbrReturnId = "RPC1004";

        public const string RpcOperationRefArgumentId = "RPC1005";

        public const string RpcOperationServiceArgumentId = "RPC1006";

        public const string ServerSideTypeIsNotRpcService = "RPC1007";

        public const string NoMatchingServerSideMember = "RPC1008";


        private const string Category = "RPC";

        internal static DiagnosticDescriptor RpcDuplicateOperationRule = new DiagnosticDescriptor(
            RpcDuplicateOperationRuleId,
            "Operation has already been defined.",
            "Operation '{0}' has been defined more than once in server side RPC definition.",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Server side RPC operation must have a unique name.");

        internal static DiagnosticDescriptor RpcMissingServerOperationRule = new DiagnosticDescriptor(
            RpcMissingServerOperationRuleId,
            "Operation does not exist in server side RPC definition.",
            "Operation '{0}' does not exist in server side RPC definition.",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Client side RPC operation must have a matching server side operation.");

        internal static DiagnosticDescriptor RpcOperationMbrArgumentRule = new DiagnosticDescriptor(
            RpcOperationMbrArgumentId,
            "MarshalByRefObject cannot be used as parameter in RPC operation.",
            "MarshalByRefObject '{0}' cannot be used as parameter in RPC operation.",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "MarshalByRefObjects cannot be used as parameter in RPC operation.");

        internal static DiagnosticDescriptor RpcOperationMbrReturnRule = new DiagnosticDescriptor(
            RpcOperationMbrReturnId,
            "MarshalByRefObject cannot be returned from an RPC operation.",
            "MarshalByRefObject '{0}' cannot be returned from RPC operation.",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "MarshalByRefObjects cannot be returned from RPC operation.");

        internal static DiagnosticDescriptor RpcOperationRefArgumentRule = new DiagnosticDescriptor(
            RpcOperationRefArgumentId,
            "An RPC operation parameter cannot be passed as reference (ref/in/out).",
            "RPC operation parameter '{0}' cannot be passed as reference.",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "An RPC operation parameter cannot be passed as reference (ref/in/out).");

        internal static DiagnosticDescriptor RpcOperationServiceArgumentRule = new DiagnosticDescriptor(
            RpcOperationServiceArgumentId,
            "RPC service cannot be used as parameter in RPC operation.",
            "Rpc service '{0}' cannot be used as parameter in RPC operation.",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "RPC services cannot be used as parameter in RPC operation.");

        internal static DiagnosticDescriptor ServerSideTypeIsNotRpcServiceRule = new DiagnosticDescriptor(
            ServerSideTypeIsNotRpcService,
            "Server side type is not an RPC service.",
            "Server side type  '{0}' s not an RPC service.",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Server side types must be marked with the [RpcService] attribute.");

        internal static DiagnosticDescriptor NoMatchingServerSideMemberRule = new DiagnosticDescriptor(
            NoMatchingServerSideMember,
            "A matching server side member does no exist.",
            "A server side operation matching '{0}' does not exist on '{1}'.",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "All members in a client side service must have a matching server side member.");
    }

    internal class GeneratedProxyType
    {
        internal readonly string ProxyTypeName;
        internal readonly string TypeCode;
        internal readonly string DictionaryEntryCode;

        internal GeneratedProxyType(string proxyTypeName, string typeCode, string dictionaryEntryCode)
        {
            this.ProxyTypeName = proxyTypeName;
            this.TypeCode = typeCode;
            this.DictionaryEntryCode = dictionaryEntryCode;
        }
    }

}
