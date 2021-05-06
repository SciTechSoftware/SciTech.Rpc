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


namespace SciTech.Rpc.Server
{
    /// <summary>
    /// Provides access to the current <see cref="IRpcServerContext"/>, if one is available.
    /// </summary>
    /// <remarks>
    /// This interface should be used with caution. It relies on AsyncLocal<T> which can have a negative performance impact on async calls. 
    /// It also creates a dependency on "ambient state" which can make testing more difficult.
    /// </remarks>
    public interface IRpcContextAccessor
    {
        /// <summary>
        /// Gets the current <see cref="IRpcServerContext"/>. Returns <c>null</c> if there is no active context.
        /// </summary>
        /// <value>The current <see cref="IRpcServerContext"/>, or <c>null</c> if there is no active context.</value>
        IRpcServerContext? RpcContext { get; }
    }
}
