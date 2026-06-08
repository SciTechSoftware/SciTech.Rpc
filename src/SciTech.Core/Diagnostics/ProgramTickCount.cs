using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace SciTech.Diagnostics
{
    public static class ProgramTickCount
    {
        private const int CounterIntervalMs = 100;
        private static readonly TimeSpan MaxExpectedSleepDuration = TimeSpan.FromMilliseconds(200);

        private static TickCount countedTicks;
        private static bool isDebugging;

        private static TickCount sleepStartTicks;

        [SuppressMessage("Performance", "CA1810:Initialize reference type static fields inline")]
        static ProgramTickCount()
        {
            isDebugging = System.Diagnostics.Debugger.IsAttached;
            if (isDebugging)
            {
                var counterThread = new Thread(RunCounter);
                counterThread.IsBackground = true;
                counterThread.Start();
            }
        }

        public static TickCount Now
        {
            get
            {
                if (!isDebugging)
                {
                    return TickCount.Now;
                }

                // Tried to add duration since last sleep start, but
                // it's a bit hard to guarantee that tick counts don't decrease.
                // Let's just return the countedTicks. This tick counter should
                // only be used for timeouts, not accurate timing.
                return countedTicks;
            }
        }

        private static void RunCounter()
        {
            while (true)
            {
                sleepStartTicks = TickCount.Now;
                Thread.Sleep(CounterIntervalMs);
                var sleepDuration = TickCount.Now - sleepStartTicks;
                if (sleepDuration < MaxExpectedSleepDuration)
                {
                    countedTicks += sleepDuration;
                }
                else
                {
                    // That's too long. Maybe we have been stopped at a breakpoint
                    // Let's just assume we have been running for CounterIntervalMs
                    countedTicks += TimeSpan.FromMilliseconds(CounterIntervalMs);
                }
            }
        }
    }
}