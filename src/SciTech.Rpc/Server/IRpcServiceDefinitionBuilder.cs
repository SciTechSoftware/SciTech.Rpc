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

using SciTech.Rpc.Server;
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

        IRpcServiceDefinitionBuilder RegisterService<TService>();

        IRpcServiceDefinitionBuilder RegisterService(Type serviceType);

        IRpcServiceDefinitionBuilder RegisterSingletonService<TService>();
    }

    public interface IRpcServiceDefinitionsProvider
    {
        event EventHandler<RpcServicesEventArgs> ServicesRegistered;

        RpcServerFaultHandler? CustomFaultHandler { get; }

        void Freeze();

        bool IsFrozen { get; }

        IImmutableList<Type> GetRegisteredServiceTypes();

        bool IsServiceRegistered(Type serviceType);
    }


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
