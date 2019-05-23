using System;

namespace SciTech.Diagnostics
{
    /// <summary>
    /// The logger manager contains static method for retrieving 
    /// loggers.
    /// </summary>
    public static class LoggerManager
    {
        public static IDetailedLogger GetLogger(Type type)
        {
            return NullLogger.Instance;
        }

        public static IDetailedLogger GetLogger(string name)
        {
            return NullLogger.Instance;
        }

        public static void Init()
        {
        }

        public static void Shutdown()
        {
        }
    }
}
