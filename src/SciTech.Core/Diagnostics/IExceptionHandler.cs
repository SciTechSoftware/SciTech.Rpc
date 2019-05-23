using System;
using System.Linq;

namespace SciTech.Diagnostics
{
    /// <summary>
    /// Defines how an unexpected class should be treated when calling <see cref="IExceptionHandler.HandleException(Exception, bool)"/>.
    /// </summary>
    public enum UnexpectedExceptionAction
    {
        /// <summary>
        /// Unexpected exceptions are not handled. <see cref="IExceptionHandler.HandleException"/> will return false in
        /// case of an unexpected exception.
        /// </summary>
        None,

        /// <summary>
        /// Unexpected exceptions are re-thrown by  <see cref="IExceptionHandler.HandleException"/>. This will make exception handling code a bit simpler,
        /// but the original exception stack trace may be lost.
        /// </summary>
        Throw,

        /// <summary>
        /// Unexpected exceptions are handled by  <see cref="IExceptionHandler.HandleException"/>. Normally they are handled by calling <see cref="IExceptionHandler.UnhandledException(Exception)"/>:
        /// </summary>
        Handle
    }

    public interface IExceptionHandler
    {
        bool HandleException(Exception e, UnexpectedExceptionAction unexpectedExceptionAction = UnexpectedExceptionAction.None);

        void UnhandledException(Exception e);
    }
}
