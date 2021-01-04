using System.Collections.Immutable;

namespace SciTech.Collections.Immutable
{
    public static class ImmutableArrayExtensions
    {
        public static ImmutableArrayList<T> ToImmutableArrayList<T>(this ImmutableArray<T> immutableArray)
        {
            return new ImmutableArrayList<T>(immutableArray);
        }

        public static ImmutableArrayList<T> ToImmutableArrayList<T>(this ImmutableArray<T>.Builder immutableArrayBuilder)
        {
            if (immutableArrayBuilder is null) throw new System.ArgumentNullException(nameof(immutableArrayBuilder));

            return new ImmutableArrayList<T>(immutableArrayBuilder.ToImmutable());
        }
    }

}
