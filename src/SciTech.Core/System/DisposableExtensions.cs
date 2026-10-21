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

using System.IO;
using System.Threading.Tasks;

namespace System
{
    public static class DisposableExtensions
    {
#if !PLAT_ASYNC_DISPOSE
        public static ValueTask DisposeAsync(this Stream disposable)
        {
            disposable?.Dispose();
            return default;
        }
#endif
    }
}
