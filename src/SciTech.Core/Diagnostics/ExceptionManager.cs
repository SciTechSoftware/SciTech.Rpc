using System;
using System.Globalization;
using System.Threading;

namespace SciTech.Diagnostics
{
    public static class ExceptionManager
    {
        private static volatile SciTech.Diagnostics.ILogger? exceptionLogger;

        public static event ThreadExceptionEventHandler? UnhandledException;

        public static SciTech.Diagnostics.ILogger? ExceptionLogger
        {
            get { return exceptionLogger; }
            set
            {
                exceptionLogger = value;
            }
        }

        public static bool IsFatalException(Exception e)
        {
            return
                /*e is OutOfMemoryException || */e is ThreadAbortException ||
                e is StackOverflowException || e is InvalidProgramException;
        }

        public static bool UnexpectedException(Exception e)
        {
            return UnexpectedException(e, true);
        }

        /// <summary>
        /// This method should be called to log an unexpected exception. 
        /// </summary>
        /// <param name="e">The unexpected exception.</param>
        /// <param name="safeToResume">Indicates whether the state of the application is known to be correct.
        /// This parameter is ignored if the supplied Exception is a fatal exception.</param>
        public static bool UnexpectedException(Exception e, bool safeToResume)
        {
            if (IsFatalException(e))
            {
                safeToResume = false;
            }

            exceptionLogger?.Error(string.Format(CultureInfo.InvariantCulture, "Unexpected exception caught (safeToResume={0})", safeToResume), e);

            if (!(e is ThreadAbortException))
            {
                UnhandledException?.Invoke(null!, new ThreadExceptionEventArgs(e));
            }

            return !safeToResume;
        }
    }
}