namespace SciTech.Rpc.Tests
{
    /// <summary>
    /// Ports and pipe names used by the tests. The tests of the different target frameworks run in parallel
    /// (e.g. when the multi-targeted project is tested with "dotnet test"), so each target framework uses its own
    /// ports and pipe names. The ports are fixed rather than allocated by the OS, so that they are outside the
    /// ephemeral port range and cannot be taken by an outgoing connection while a test server is restarted.
    /// </summary>
    internal static class TestEndPoints
    {
#if NET10_0
        private const int FrameworkIndex = 3;
#elif NET9_0
        private const int FrameworkIndex = 2;
#elif NET8_0
        private const int FrameworkIndex = 1;
#else
        private const int FrameworkIndex = 0;
#endif

        internal const int TcpPort = 15900 + FrameworkIndex * 10;

        /// <summary>
        /// Port of an additional server, for tests that use two servers.
        /// </summary>
        internal const int TcpPort2 = TcpPort + 1;

        internal const int AuthenticationTcpPort = TcpPort + 2;

        internal const int GrpcPort = TcpPort + 3;

        internal static readonly string PipeName = "SciTech.Rpc.Tests." + FrameworkIndex;
    }
}
