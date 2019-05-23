﻿#region Copyright notice and license
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

namespace SciTech.Rpc.Client
{
    public class RpcProxyServicesBuilder : IRpcProxyDefinitionsBuilder
    {
        private readonly Dictionary<string, IRpcClientExceptionConverter> exceptionConverters = new Dictionary<string, IRpcClientExceptionConverter>();

        private readonly Dictionary<string, List<Type>> knownServices = new Dictionary<string, List<Type>>();

        public RpcProxyServicesBuilder()
        {
        }

        public RpcProxyServicesBuilder(IEnumerable<IRpcServiceRegistration> registrations, IEnumerable<IRpcClientExceptionConverter> exceptionConverters)
        {
            if (registrations != null)
            {
                foreach (var registration in registrations)
                {
                    foreach (var serviceType in registration.GetServiceTypes(RpcServiceDefinitionType.Client))
                    {
                        this.RegisterKnownService(serviceType);
                    }
                }
            }

            if (exceptionConverters != null)
            {
                foreach (var converter in exceptionConverters)
                {
                    this.RegisterExceptionConverter(converter);
                }
            }

        }

        public IRpcClientExceptionConverter GetExceptionConverter(string faultCode)
        {
            this.exceptionConverters.TryGetValue(faultCode, out var converter);
            return converter;
        }

        public IReadOnlyList<Type> GetServicesTypes(string serviceName)
        {
            if (this.knownServices.TryGetValue(serviceName, out var services))
            {
                // TODO: Either freeze registration after service types has been 
                // retrieved, or use an immutable collection.
                return services;
            }

            return Array.Empty<Type>();
        }

        public void RegisterExceptionConverter(IRpcClientExceptionConverter exceptionConverter)
        {
            this.exceptionConverters.Add(exceptionConverter.FaultCode, exceptionConverter);
        }

        public void RegisterKnownService<TService>() where TService : class
        {
            this.RegisterKnownService(typeof(TService));
        }

        private void RegisterKnownService(Type serviceType)
        {
            var interfaceServices = RpcBuilderUtil.GetAllServices(serviceType, RpcServiceDefinitionType.Client, false);
            foreach (var serviceInfo in interfaceServices)
            {
                if (this.knownServices.TryGetValue(serviceInfo.FullName, out var services))
                {
                    if (services.Find(s => s.Equals(serviceInfo.Type)) == null)
                    {
                        services.Add(serviceInfo.Type);
                    }
                }
                else
                {
                    services = new List<Type>();
                    services.Add(serviceInfo.Type);
                    this.knownServices.Add(serviceInfo.FullName, services);
                }
            }
        }
    }
}
