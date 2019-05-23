using System;

namespace SciTech.Diagnostics
{
    public interface IDetailedLogger : ILogger
    {
        void Debug(string message, string detailsText);

#pragma warning disable CA1716 // Identifiers should not match keywords
        void Error(string message, string detailsText);

#pragma warning restore CA1716 // Identifiers should not match keywords

        void Fatal(string message, string detailsText);

        void Info(string message, string detailsText);

        void Warn(string message, string detailsText);
    }

    /// <summary>
    /// The ILogger interfaces provides access to a logger.
    /// One way of retrieving an ILogger interface is by calling <see cref="LoggerManager.GetLogger"/>.
    /// </summary>
    public interface ILogger
    {
        bool IsDebugEnabled { get; }

        bool IsErrorEnabled { get; }

        bool IsFatalEnabled { get; }

        bool IsInfoEnabled { get; }

        bool IsWarnEnabled { get; }

        void Debug(string message);

        void Debug(string message, Exception t);

        void Fatal(string message);
        void Fatal(string message, Exception t);

        void Info(string message);

        void Info(string message, Exception t);

        void Warn(string message);

        void Warn(string message, Exception t);

#pragma warning disable CA1716 // Identifiers should not match keywords
        void Error(string message, Exception t);

        void Error(string message);

#pragma warning restore CA1716 // Identifiers should not match keywords

    }
}
