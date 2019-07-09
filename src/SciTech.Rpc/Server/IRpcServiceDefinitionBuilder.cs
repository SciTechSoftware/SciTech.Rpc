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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;

namespace SciTech.Rpc.Server
{
    public interface IRpcServiceDefinitionBuilder : IRpcServiceDefinitionsProvider
    {
        IRpcServiceDefinitionBuilder RegisterAssemblyServices(params Assembly[] assemblies);

        IRpcServiceDefinitionBuilder RegisterExceptionConverter(IRpcServerExceptionConverter exceptionConverter);

        IRpcServiceDefinitionBuilder RegisterService<TService>(RpcServiceOptions? options = null);

        IRpcServiceDefinitionBuilder RegisterService(Type serviceType, RpcServiceOptions? options = null);
    }

    public interface IRpcServiceDefinitionsProvider
    {
        event EventHandler<RpcServicesEventArgs> ServicesRegistered;

        ImmutableArray<RpcServerCallInterceptor> CallInterceptors { get; }

        /// <summary>
        /// Gets the custom <see cref="RpcServerFaultHandler"/> that has been initialized 
        /// using the <see cref="ExceptionConverters"/>. If there are no <c>ExceptionConverters</c>
        /// this property will return <c>null</c>.
        /// </summary>
        /// <value>An <see cref="RpcServerFaultHandler"/> that has been initialized 
        /// using the <see cref="ExceptionConverters"/> , or <c>null</c> if there are no exception converters.</value>
        RpcServerFaultHandler? CustomFaultHandler { get; }

        /// <summary>
        /// Gets the exceptions converters associated with this definitions provider. If the array is 
        /// not empty, the <see cref="CustomFaultHandler"/> will represent the fault handler
        /// for the combined exception converters.
        /// </summary>
        ImmutableArray<IRpcServerExceptionConverter> ExceptionConverters { get; }

        bool IsFrozen { get; }

        ImmutableRpcServiceOptions Options { get; }

        void Freeze();

        IImmutableList<Type> GetRegisteredServiceTypes();

        bool IsServiceRegistered(Type serviceType);
    }

    /// <summary>
    /// TODO: No longer used, should probably be removed.
    /// </summary>
    public interface IRpcServiceRegistrator
    {
        void RegisterServices(IRpcServiceDefinitionBuilder builder);
    }

    public class RpcServicesEventArgs : EventArgs
    {
        internal RpcServicesEventArgs(IReadOnlyList<Type> serviceTypes)
        {
            this.ServiceTypes = serviceTypes;
        }

        public IReadOnlyList<Type> ServiceTypes { get; }
    }
}
