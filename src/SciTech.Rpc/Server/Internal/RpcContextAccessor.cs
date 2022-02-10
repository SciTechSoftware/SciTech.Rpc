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


using System.Threading;

namespace SciTech.Rpc.Server.Internal
{
    /// <summary>
    /// Default implementation of IRpcContextAccessor. 
    /// </summary>
    public class RpcContextAccessor : IRpcContextAccessor
    {
        private static AsyncLocal<RpcContextHolder> rpcContextCurrent = new AsyncLocal<RpcContextHolder>();

        /// <inheritdoc/>
        public IRpcServerContext? RpcContext
        {
            get
            {
                return rpcContextCurrent.Value?.Context;
            }
        }

        internal static void Init(IRpcServerContext value)
        {
            if (value != null)
            {
                // Use an object indirection to hold the IRpcServerContext in the AsyncLocal,
                // so it can be cleared in all ExecutionContexts when its cleared.
                rpcContextCurrent.Value = new RpcContextHolder { Context = value };
            }
        }
         
        internal static void Clear()
        {
            var holder = rpcContextCurrent.Value;
            if (holder != null)
            {
                // Clear current IRpcServerContext trapped in the AsyncLocals, as its done.
                holder.Context = null;
            }
        }

        private class RpcContextHolder
        {
            public IRpcServerContext? Context;
        }

    }
}
