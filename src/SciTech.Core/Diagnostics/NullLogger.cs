using System;

namespace SciTech.Diagnostics
{
    public sealed class NullLogger : IDetailedLogger
    {
        public static readonly IDetailedLogger Instance = new NullLogger();

        public bool IsDebugEnabled
        {
            get
            {
                return false;
            }
        }

        public bool IsErrorEnabled
        {
            get
            {
                return false;
            }
        }

        public bool IsFatalEnabled
        {
            get
            {
                return false;
            }
        }

        public bool IsInfoEnabled
        {
            get
            {
                return false;
            }
        }

        public bool IsWarnEnabled
        {
            get
            {
                return false;
            }
        }

        public void Debug(string message, Exception t)
        {
        }

        public void Error(string message, Exception t)
        {
        }

        public void Fatal(string message, Exception t)
        {
        }

        public void Info(string message, Exception t)
        {
        }

        public void Warn(string message, Exception t)
        {
        }

        void SciTech.Diagnostics.ILogger.Debug(string message)
        {
        }

        void SciTech.Diagnostics.IDetailedLogger.Debug(string message, string detailsText)
        {
        }

        void SciTech.Diagnostics.ILogger.Error(string message)
        {
        }

        void SciTech.Diagnostics.IDetailedLogger.Error(string message, string detailsText)
        {
        }

        void SciTech.Diagnostics.ILogger.Fatal(string message)
        {
        }

        void SciTech.Diagnostics.IDetailedLogger.Fatal(string message, string detailsText)
        {
        }

        void SciTech.Diagnostics.ILogger.Info(string message)
        {
        }

        void SciTech.Diagnostics.IDetailedLogger.Info(string message, string detailsText)
        {
        }

        void SciTech.Diagnostics.ILogger.Warn(string message)
        {
        }

        void SciTech.Diagnostics.IDetailedLogger.Warn(string message, string detailsText)
        {
        }
    }
}
