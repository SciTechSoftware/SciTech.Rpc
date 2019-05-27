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

using SciTech.Collections;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciTech.Rpc.Client.Internal
{
    public interface IAsyncStreamingServerCall<TResponse> : IDisposable
        where TResponse : class
    {
        IAsyncStream<TResponse> ResponseStream { get; }
    }

   
}
