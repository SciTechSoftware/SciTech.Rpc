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

using System;

namespace SciTech.Rpc.Internal
{
    internal static class WellKnownRpcErrors
    {
        public const string Failure = "SciTech.Rpc.RpcFailureError";

        public const string Fault = "SciTech.Rpc.RpcFaultError";

        public const string ServiceUnavailable = "SciTech.Rpc.RpcServiceUnavailableError";
    }

}
