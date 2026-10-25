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

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SciTech.Diagnostics
{
    public static class ExceptionExtensions
    {
        /// <summary>
        /// Gets a value indicating whether <paramref name="exception"/> leaves the process in a state where it should not
        /// be caught and ignored. Intended for exception filters, e.g. <c>catch (Exception e) when (!e.IsFatal())</c>.
        /// </summary>
        /// <remarks>
        /// <see cref="InvalidProgramException"/> is considered fatal, since it indicates an error in generated code
        /// that should not be hidden. <see cref="InsufficientMemoryException"/> is not considered fatal, since it is thrown
        /// before any memory has been allocated.
        /// </remarks>
        public static bool IsFatal(this Exception exception)
        {
            if (exception is null) throw new ArgumentNullException(nameof(exception));

            return exception is OutOfMemoryException and not InsufficientMemoryException
                or ThreadAbortException
                or StackOverflowException
                or AccessViolationException
                or SEHException
                or InvalidProgramException;
        }
    }
}
