using System.Collections.Immutable;

namespace SciTech.Collections.Immutable
{
    public static class ImmutableArrayExtensions
    {
        public static ImmutableArrayList<T> ToImmutableArrayList<T>(this ImmutableArray<T> immutableArray)
        {
            return new ImmutableArrayList<T>(immutableArray);
        }
    }

}
