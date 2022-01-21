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

        /// <summary>
        /// Initializes a global logger factory for all RPC components. If a logger is not provided 
        /// for an RPC component, this factory will be used to create a one.
        /// </summary>
        /// <param name="loggerFactory">The factory to use when creating loggers for RPC components.</param>
        public static void InitLogger( ILoggerFactory loggerFactory)
        {
            RpcLogger.loggerFactory = loggerFactory;               
        }

        internal static ILogger<TCategoryName>? TryCreateLogger<TCategoryName>()
        {
            return loggerFactory?.CreateLogger<TCategoryName>();
        }
        
        internal static ILogger? TryCreateLogger(Type categoryType)
        {
            return loggerFactory?.CreateLogger(categoryType);
        }

        internal static ILogger<TCategoryName> CreateLogger<TCategoryName>()
        {
            return loggerFactory?.CreateLogger<TCategoryName>() ?? NullLogger<TCategoryName>.Instance;
        }

        internal static ILogger CreateLogger(Type categoryType)
        {
            return loggerFactory?.CreateLogger(categoryType) ?? NullLogger.Instance;
        }

    }
}
