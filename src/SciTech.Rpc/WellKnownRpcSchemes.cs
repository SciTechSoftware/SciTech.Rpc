#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion


namespace SciTech.Rpc
{
    /// <summary>
    /// Defines well known names for schemes implemented by SciTech RPC.
    /// </summary>
    public static class WellKnownRpcSchemes
    {
        public const string Grpc = "grpc";

        public const string LightweightTcp = "lightweight.tcp";

        public const string LightweightPipe = "lightweight.pipe";
    }
}
