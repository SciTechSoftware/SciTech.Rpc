#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using SciTech.Rpc.Internal;
using SciTech.Rpc.Serialization;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;

namespace SciTech.Rpc.Server
{
    public interface IRpcServiceDefinitionsBuilder // : IRpcServiceDefinitionsProvider
    {
        IRpcServiceDefinitionsBuilder RegisterAssemblyServices(params Assembly[] assemblies);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="serviceType"></param>
        /// <param name="implementationType">Optional type of the service implementation. This type will be used when retrieving attributes for the 
        /// service, e.g. related to authorization.</param>
        /// <param name="options"></param>
        /// <returns></returns>
        IRpcServiceDefinitionsBuilder RegisterService(Type serviceType, Type? implementationType, IRpcServerOptions? options = null);

        IRpcServiceDefinitionsBuilder RegisterImplementation(Type implementationType, IRpcServerOptions? options = null);
    }

    public interface IRpcServiceDefinitionsProvider
    {
        event EventHandler<RpcServicesEventArgs> ServicesRegistered;

        bool IsFrozen { get; }

        /// <summary>
        /// Freezes the provider to prevent additional services from being registered. Normally
        /// called by <see cref="IRpcServer"/> implementations that do not support additional
        /// services after the server has been started.
        /// </summary>
        void Freeze();

        IImmutableList<Type> GetRegisteredServiceTypes();

        IRpcServerOptions? GetServiceOptions(Type serviceType);

        bool IsServiceRegistered(Type serviceType);
        
        RpcServiceInfo? GetRegisteredServiceInfo(Type serviceType);
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
