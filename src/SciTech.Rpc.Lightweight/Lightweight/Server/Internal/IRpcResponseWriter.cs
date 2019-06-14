﻿#region Copyright notice and license
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
using System;
using System.IO;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server.Internal
{
    internal interface IRpcResponseWriter
    {
        void AbortWrite(Exception? exception);

        ValueTask<Stream> BeginWriteAsync(in LightweightRpcFrame responseHeader);

        ValueTask EndWriteAsync();
    }
}