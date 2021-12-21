using System;
using System.Collections.Generic;
using System.Text;

namespace SciTech.Threading
{
    public class ExceptionEventArgs : EventArgs
    {
        public ExceptionEventArgs(Exception exception)
        {
            this.Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        }

        public Exception Exception { get; }
    }
}
