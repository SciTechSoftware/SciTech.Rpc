using NUnit.Framework;
using System;
using System.Threading;

namespace SciTech.Rpc.Tests
{
    [SetUpFixture]
    public class TestAssemblySetup
    {
        /// <summary>
        /// Many tests make blocking calls, and some test services block in their implementation (e.g. with
        /// <see cref="Thread.Sleep(TimeSpan)"/>). On machines with few cores, e.g. CI runners that also run the tests of
        /// several target frameworks in parallel, the thread pool then injects threads too slowly, which delays timers
        /// and continuations and makes timing dependent tests fail.
        /// </summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ThreadPool.GetMinThreads(out int workerThreads, out int completionPortThreads);
            ThreadPool.SetMinThreads(Math.Max(workerThreads, 64), Math.Max(completionPortThreads, 64));
        }
    }
}
