#region Copyright notice and license
// Copyright (c) SciTech Software AB and contributors.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using System;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace SciTech.Rpc.Server
{
    public delegate Task<IDisposable> RpcServerCallInterceptor(IRpcServerContextBuilder metadata);
}
