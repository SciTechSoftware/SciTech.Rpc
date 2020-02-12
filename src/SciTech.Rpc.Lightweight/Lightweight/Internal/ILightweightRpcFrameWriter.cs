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

using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Serialization;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Internal
{
    internal interface ILightweightRpcFrameWriter
    {
        LightweightRpcFrame.WriteState BeginWrite(in LightweightRpcFrame responseHeader);

        void AbortWrite( in LightweightRpcFrame.WriteState state );

        ValueTask EndWriteAsync( in LightweightRpcFrame.WriteState state, bool throwOnError );
    }

}
