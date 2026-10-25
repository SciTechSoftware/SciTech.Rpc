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

using Microsoft.Extensions.Logging;
using SciTech.Rpc.Logging;

namespace SciTech.Rpc.Resources
{
    /// <summary>
    /// Logging for the client side of acquired resources. There is no service provider available when an acquired resource
    /// is deserialized or finalized, so the loggers are created by the global factory (see <see cref="RpcLogger.InitLogger"/>).
    /// </summary>
    internal static class ResourcesLog
    {
        internal static readonly Action<ILogger, Exception?> UnreleasedResourceFinalized = LoggerMessage.Define(LogLevel.Warning,
            new EventId(1, nameof(UnreleasedResourceFinalized)), "An unreleased acquired resource has been finalized.");

        internal static readonly Action<ILogger, Exception?> ReleaseFailed = LoggerMessage.Define(LogLevel.Warning,
            new EventId(2, nameof(ReleaseFailed)), "Failed to release acquired resource.");

        internal static readonly Action<ILogger, Exception?> LifetimeServiceRefMissing = LoggerMessage.Define(LogLevel.Warning,
            new EventId(3, nameof(LifetimeServiceRefMissing)), "The lifetime service reference of an acquired resource is not initialized. Serialization error?");

        internal static readonly Action<ILogger, Exception?> KeepAliveFailed = LoggerMessage.Define(LogLevel.Warning,
            new EventId(4, nameof(KeepAliveFailed)), "Failed to keep acquired resource alive.");

        internal static readonly Action<ILogger, Exception?> AcquiredResourceLost = LoggerMessage.Define(LogLevel.Information,
            new EventId(5, nameof(AcquiredResourceLost)), "Acquired resource timed out or was taken over by another client. Removing acquired resource.");

        internal static readonly Action<ILogger, Exception?> KeepAliveTimerFailed = LoggerMessage.Define(LogLevel.Error,
            new EventId(6, nameof(KeepAliveTimerFailed)), "Unexpected error when keeping acquired resources alive.");
    }
}
