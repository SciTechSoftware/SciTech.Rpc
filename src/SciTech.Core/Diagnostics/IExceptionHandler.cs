using System;
using System.Linq;

namespace SciTech.Diagnostics
{
    /// <summary>
    /// Defines how an unexpected class should be treated when calling <see cref="IExceptionHandler.HandleException(Exception, UnexpectedExceptionAction)"/>.
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
        /// <summary>
        /// Tries to handle the provided exception. If the exception cannot be handled, performs the action specified
        /// by <paramref name="unexpectedExceptionAction"/>. 
        /// </summary>
        /// <param name="e">The exception to handle.</param>
        /// <param name="unexpectedExceptionAction">Specifies the action to perform for an unexpected exception. For more information, see <see cref="UnexpectedExceptionAction"/>.</param>
        /// <returns><c>true</c> if the exception was handled or propagated to <see cref="UnhandledException(Exception)"/>. If <c>false</c>
        /// is returned, the caller needs to handle the exception, e.g. by re-throwing.</returns>
        bool HandleException(Exception e, UnexpectedExceptionAction unexpectedExceptionAction = UnexpectedExceptionAction.None);

        /// <summary>
        /// Handles an unexpected exception. An unexpected exception is normally handled by logging it, showing an error message, or re-throwing it.
        /// </summary>
        /// <param name="e"></param>
        void UnhandledException(Exception e);
    }
}
