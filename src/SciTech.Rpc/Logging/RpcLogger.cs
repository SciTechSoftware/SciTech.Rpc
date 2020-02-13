using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace SciTech.Rpc.Logging
{
    public static class RpcLogger
    {
        static ILoggerFactory? loggerFactory;

        public static void InitLogger( ILoggerFactory? loggerFactory)
        {
            RpcLogger.loggerFactory = loggerFactory;               
        }

        internal static ILogger<TCategoryName>? TryCreateLogger<TCategoryName>()
        {
            return loggerFactory?.CreateLogger<TCategoryName>();
        }

        internal static ILogger CreateLogger<TCategoryName>()
        {
            return (ILogger?)loggerFactory?.CreateLogger<TCategoryName>() ?? NullLogger.Instance;
        }
    }
}
