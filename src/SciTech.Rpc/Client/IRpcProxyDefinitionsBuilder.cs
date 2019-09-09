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

namespace SciTech.Rpc.Client
{
    public interface IRpcProxyDefinitionsProvider
    {
        /// <summary>
        /// Gets a list of known types that implement the RPC service with the specified <paramref name="serviceName"/>.
        /// </summary>
        /// <param name="serviceName">Full name of the RPC service.</param>
        /// <returns>A list of known types that implement the specified RPC service.</returns>
        IReadOnlyList<Type> GetServicesTypes(string serviceName);

        IRpcClientExceptionConverter? GetExceptionConverter(string faultCode);
    }

    public interface IRpcProxyDefinitionsBuilder : IRpcProxyDefinitionsProvider
    {
        void RegisterExceptionConverter(IRpcClientExceptionConverter exceptionConverter);

        void RegisterKnownService<TService>() where TService : class;
    }
}
