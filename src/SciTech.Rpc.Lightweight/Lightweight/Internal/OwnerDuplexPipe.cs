#region Copyright notice and license
// Copyright (c) 2019-2021, SciTech Software AB.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using System;
using System.IO.Pipelines;

namespace SciTech.Rpc.Lightweight.Internal
{
    /// <summary>
    /// Helper class that associates a IDuplexPipe with a disposable object (e.g. a Stream). 
    /// The disposable object will be disposed when the pipe is disposed.
    /// </summary>
    internal class OwnerDuplexPipe : IDuplexPipe, IDisposable
    {
        private IDisposable? disposable;

        internal OwnerDuplexPipe(IDuplexPipe pipe, IDisposable disposable)
        {
            this.disposable = disposable;
            this.Input = pipe.Input;
            this.Output = pipe.Output;
        }

        public PipeReader Input { get; }

        public PipeWriter Output { get; }

        public void Dispose()
        {
            this.disposable?.Dispose();
            this.disposable = null;
        }
    }
}
