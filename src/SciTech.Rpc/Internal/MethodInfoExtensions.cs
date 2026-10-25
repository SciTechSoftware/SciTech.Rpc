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

#if !NET
using System;
using System.Reflection;

namespace SciTech.Rpc.Internal
{
    /// <summary>
    /// Polyfills for the generic <c>MethodInfo.CreateDelegate</c> overloads, which are only available on .NET.
    /// </summary>
    internal static class MethodInfoExtensions
    {
        internal static T CreateDelegate<T>(this MethodInfo method) where T : Delegate
            => (T)method.CreateDelegate(typeof(T));

        internal static T CreateDelegate<T>(this MethodInfo method, object? target) where T : Delegate
            => (T)method.CreateDelegate(typeof(T), target);
    }
}
#endif
