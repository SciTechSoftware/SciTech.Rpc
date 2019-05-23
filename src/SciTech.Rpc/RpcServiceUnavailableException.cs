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

using System;

namespace SciTech.Rpc
{
    public class RpcServiceUnavailableException : Exception
    {
        public RpcServiceUnavailableException()
        {
        }

        public RpcServiceUnavailableException(string message) : base(message)
        {
        }

        public RpcServiceUnavailableException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
