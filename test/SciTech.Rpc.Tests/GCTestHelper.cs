using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests
{
    internal static class GCTestHelper
    {
        /// <summary>
        /// Collects garbage until the target of <paramref name="weakReference"/> has been collected.
        /// </summary>
        /// <remarks>
        /// A single collection is not enough: the calling code may run inline on a stack that still
        /// references the target (e.g. a synchronously completed await, or a continuation invoked
        /// from server code), and unoptimized code keeps such references alive until the frame returns.
        /// Awaiting a delay before each collection makes sure the collection runs on a fresh stack.
        /// </remarks>
        internal static async Task CollectAsync(WeakReference weakReference)
        {
            for (int i = 0; i < 50 && weakReference.IsAlive; i++)
            {
                await Task.Delay(10);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            Assert.IsFalse(weakReference.IsAlive, "Object was not garbage collected.");
        }
    }
}
