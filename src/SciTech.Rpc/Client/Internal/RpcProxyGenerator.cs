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

using SciTech.Collections.Generic;
using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace SciTech.Rpc.Client.Internal
{
    /// <summary>
    /// </summary>
    /// <typeparam name="TRpcProxy"></typeparam>
    /// <typeparam name="TProxyArgs"></typeparam>
    /// <typeparam name="TMethodDef"></typeparam>
    public abstract class RpcProxyGenerator<TRpcProxy, TProxyArgs, TMethodDef> : IRpcProxyGenerator
        where TRpcProxy : RpcProxyBase<TMethodDef>
        where TMethodDef : RpcProxyMethod
        where TProxyArgs : RpcProxyArgs
    {
        private readonly Dictionary<HashSetKey<string>, Delegate> generatedFactories = new Dictionary<HashSetKey<string>, Delegate>();

        private readonly object syncRoot = new object();

        private ModuleBuilder? moduleBuilder;

        /// <summary>
        /// Contains information about the generated proxy type names defined using <see cref="moduleBuilder"/>.
        /// </summary>
        private Dictionary<string, int>? definedProxyTypes;

        protected RpcProxyGenerator()
        {
        }

        public RpcObjectProxyFactory GenerateObjectProxyFactory<TService>(
            IReadOnlyCollection<string>? implementedServices,
            IReadOnlyDictionary<string,ImmutableArray<Type>>? knownServiceTypes )
            where TService : class
        {
            lock (this.syncRoot)
            {
                var serviceInterfaces = this.GetAllServices<TService>(implementedServices, knownServiceTypes);
                var key = new HashSetKey<string>(serviceInterfaces.Select(s => s.FullName));

                // If a proxy with the same set of service interfaces has been generated before
                // let's reuse that one.
                if (this.generatedFactories.TryGetValue(key, out var currFactory))
                {
                    // TODO: Should maybe look for a factory which has a superset of implemented interfaces?
                    return (RpcObjectProxyFactory)currFactory;
                }

                var (moduleBuilder, definedProxyTypes) = CreateModuleBuilder();

                var proxyTypeBuilder = new RpcServiceProxyBuilder<TRpcProxy, TMethodDef>(
                    serviceInterfaces,
                    moduleBuilder, definedProxyTypes);
                (Func<TProxyArgs, TMethodDef[], RpcProxyBase> proxyCreator, Func<TMethodDef[]> createMethodsFunc) 
                    = proxyTypeBuilder.BuildObjectProxyFactory<TProxyArgs>();
                
                // Actually generating a method that creates the method definitions array (createMethodsFunc) instead of 
                // building a TMethodDef array directly is definitely unnecessary at the moment, but may be useful in the future, mainly in AOT scenarios.
                // Using this implementation, it would be possible to pre-generate code from proxyCreator and createMethodsFunc
                // and then this method could use something like LookupObjectProxyFactory instead of BuildObjectProxyFactory.
                // This will avoid dynamic code generation.
                TMethodDef[] methodDefs = createMethodsFunc();
                var proxyMethods = methodDefs;

                RpcObjectProxyFactory newFactory = this.CreateProxyFactory(proxyCreator, implementedServices, proxyMethods);

                this.generatedFactories.Add(key, newFactory);

                return newFactory;
            }
        }

        private (ModuleBuilder, Dictionary<string, int>) CreateModuleBuilder()
        {
            if (this.moduleBuilder == null)
            {
                var assemblyName = Guid.NewGuid().ToString();
#if PLAT_SUPPORT_COLLECTIBLE_ASSEMBLIES
                var builderAccess = AssemblyBuilderAccess.RunAndCollect;
#else
                // RunAndCollect causes tests to crash on .NET Core 2.0 and .NET Core 2.1. It seems
                // like assemblies are collected while still being in use.
                var builderAccess = AssemblyBuilderAccess.Run;
#endif
                var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName), builderAccess);
                this.moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName);
                this.definedProxyTypes = new Dictionary<string, int>();
            }

            return (this.moduleBuilder,this.definedProxyTypes!);
        }

        /// <summary>
        /// Gets all services that should be implemented by the proxy, based on information about the requested <typeparamref name="TService"/>,
        /// the implemented services on the server side, and registered proxy types.
        /// </summary>
        /// <typeparam name="TService"></typeparam>
        /// <param name="implementedServices"></param>
        /// <returns></returns>
        private List<RpcServiceInfo> GetAllServices<TService>(
            IReadOnlyCollection<string>? implementedServices, 
            IReadOnlyDictionary<string, ImmutableArray<Type>>? knownServiceTypes)
        {
            var interfaceServices = RpcBuilderUtil.GetAllServices(typeof(TService), RpcServiceDefinitionSide.Client, false);
            if (implementedServices?.Count > 0)
            {
                // We have information about implemented services on the server side.
                // Make sure that the interfaceServices are actually implemented
                foreach (var interfaceService in interfaceServices)
                {
                    if (!implementedServices.Any(s => s == interfaceService.FullName))
                    {
                        throw new RpcServiceUnavailableException($"Service '{interfaceService.FullName}' is not implemented.");
                    }
                }

                // And add all known service interfaces.
                if (knownServiceTypes != null)
                {
                    foreach (var implementedService in implementedServices)
                    {
                        if (knownServiceTypes.TryGetValue(implementedService, out var knownTypes))
                        {
                            foreach (var serviceType in knownTypes)
                            {
                                if (interfaceServices.Find(s => s.Type.Equals(serviceType)) == null)
                                {
                                    var serviceInfo = RpcBuilderUtil.GetServiceInfoFromType(serviceType);
                                    interfaceServices.Add(serviceInfo);    // serviceInfo is not null when throwOnError is true.
                                }
                            }
                        }
                    }
                }
            }

            return interfaceServices;
        }

        protected abstract RpcObjectProxyFactory CreateProxyFactory(
            Func<TProxyArgs, TMethodDef[], RpcProxyBase> proxyCreator,
            IReadOnlyCollection<string>? implementedServices,
            TMethodDef[] proxyMethods);
    }
}
