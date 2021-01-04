using System;
using System.Diagnostics;

namespace SciTech.Diagnostics
{
    /// <summary>
    /// TickCount can be used to calculate time spans that are not dependent on the
    /// system time. 
    /// </summary>
    public struct TickCount : IEquatable<TickCount>
    {
        private readonly long tickCount;

        private static readonly double tickFrequency = TicksPerSecond / Stopwatch.Frequency;

        private const long TicksPerMillisecond = 10000;

        private const long TicksPerSecond = TicksPerMillisecond * 1000;

        private TickCount(long tickCount)
        {
            this.tickCount = tickCount;
        }

        public static TickCount Now
        {
            get
            {
                return new TickCount(Stopwatch.GetTimestamp());
            }
        }

        public static TickCount Add(TickCount left, TimeSpan right)
        {
            return left + right;
        }

        public static TimeSpan Subtract(TickCount left, TickCount right)
        {
            return left - right;
        }

        public int CompareTo(TickCount other)
        {
            long ticks = this.tickCount - other.tickCount;
            return ticks.CompareTo(0);
        }

        public override bool Equals(object? obj)
        {
            return obj is TickCount other && this.Equals(other);
        }

        public bool Equals(TickCount other)
        {
            return this.tickCount == other.tickCount;
        }

        public override int GetHashCode()
        {
            return this.tickCount.GetHashCode();
        }

        public static TimeSpan operator -(TickCount count1, TickCount count2)
        {
            long ticks = count1.tickCount - count2.tickCount;
            if (Stopwatch.IsHighResolution)
            {
                double dticks = ticks * tickFrequency;
                long timeSpanTicks = unchecked((long)dticks);
                return TimeSpan.FromTicks(timeSpanTicks);
            }
            else
            {
                return TimeSpan.FromTicks(ticks);
            }
        }

        public static TickCount operator +(TickCount op1, TimeSpan op2)
        {
            if (Stopwatch.IsHighResolution)
            {
                long timeSpanTicks = op2.Ticks;
                double dticks = (double)timeSpanTicks / tickFrequency;
                long ticks = unchecked((long)dticks);

                return new TickCount(op1.tickCount + ticks);
            }
            else
            {
                return new TickCount(op1.tickCount + op2.Ticks);
            }
        }

        public static bool operator >(TickCount op1, TickCount op2)
        {
            long ticks = op1.tickCount - op2.tickCount;
            return ticks > 0;
        }

        public static bool operator <(TickCount op1, TickCount op2)
        {
            long ticks = op1.tickCount - op2.tickCount;
            return ticks < 0;
        }

        public static bool operator >=(TickCount op1, TickCount op2)
        {
            long ticks = op1.tickCount - op2.tickCount;
            return ticks >= 0;
        }

        public static bool operator <=(TickCount op1, TickCount op2)
        {
            long ticks = op1.tickCount - op2.tickCount;
            return ticks <= 0;
        }

        public static bool operator ==(TickCount left, TickCount right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(TickCount left, TickCount right)
        {
            return !(left == right);
        }
    }
}
