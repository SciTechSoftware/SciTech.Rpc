﻿#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB
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

namespace SciTech.Rpc
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Event | AttributeTargets.Property)]
    public sealed class RpcOperationAttribute : Attribute
    {
        /// <summary>
        /// Indicates whether it is allowed to execute the operation inline, e.g. in the directly on the communication 
        /// thread instead of using the associated synchronization context (the thread pool by default).
        /// </summary>
        /// <remarks>This property should only be set to <c>true</c> on fast-running performance critical
        /// methods and properties.</remarks>
        public bool AllowInlineExecution { get; set; }

        public string Name { get; set; } = "";
    }
}
