#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using System;

namespace SciTech.Rpc.NetGrpc.Server.Internal
{
    /// <summary>
    /// Helper class that forwards the ServiceProvider for the ASP.NET Core handler to the 
    /// NetGrpc implementation.
    /// </summary>
    internal class NetGrpcServiceActivator
    {
        internal readonly IServiceProvider ServiceProvider;

        internal NetGrpcServiceActivator(IServiceProvider serviceProvider)
        {
            this.ServiceProvider = serviceProvider;
        }
    }
}
