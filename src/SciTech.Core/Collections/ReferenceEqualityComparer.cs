using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SciTech.Collections
{
    public class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceEqualityComparer<T> Comparer = new ReferenceEqualityComparer<T>();

        public bool Equals([AllowNull]T x, [AllowNull] T y)
        {
            return object.ReferenceEquals(x, y);
        }

        public int GetHashCode(T obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}

