using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SciTech.Collections.Immutable;

namespace SciTech.Collections
{
    public static class ListExtensions
    {
        public static IReadOnlyList<T> AsReadOnly<T>(this IList<T> collection)
        {
            if (collection == null)
                throw new ArgumentNullException(nameof( collection ));

            return collection as IReadOnlyList<T>
                ?? new ReadOnlyWrapper<T>(collection);
        }

        public static IImmutableList<T> AsImmutable<T>(this IEnumerable<T> list)
        {
            if (list == null)
                throw new ArgumentNullException(nameof(list));

            return list as IImmutableList<T> ?? ImmutableArray.CreateRange(list);
        }

        public static ImmutableArrayList<T> AsImmutableArrayList<T>(this IEnumerable<T> list)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));

            return ImmutableArrayList.CreateRange(list);
        }


        public static ImmutableArray<TResult> SelectAsImmutableArray<T,TResult>(this IReadOnlyList<T> list, Func<T,TResult> selector)
        {
            if (list == null)
                throw new ArgumentNullException(nameof(list));
            if (selector == null)
                throw new ArgumentNullException(nameof(selector));

            int count = list.Count;
            var builder = ImmutableArray.CreateBuilder<TResult>(count);
            for( int i=0; i < count; i++ )
            {
                builder.Add(selector(list[i]));
            }

            return builder.MoveToImmutable();
        }

        public static ImmutableArray<TResult> SelectAsImmutableArray<T, TResult>(this ImmutableArray<T> list, Func<T, TResult> selector)
        {
            if (selector == null)
                throw new ArgumentNullException(nameof(selector));

            int count = list.Length;
            var builder = ImmutableArray.CreateBuilder<TResult>(count);
            for (int i = 0; i < count; i++)
            {
                builder.Add(selector(list[i]));
            }

            return builder.MoveToImmutable();
        }


        public static int IndexOf<T>( this IReadOnlyList<T> list, T item)
        {
            if (list is null) throw new ArgumentNullException(nameof(list));

            if ( list is List<T> concreteList )
            {
                return concreteList.IndexOf(item);
            }

            if( list is T[] array )
            {
                return Array.IndexOf(array, item); 
            }

            int nItems = list.Count;
            var comparer = EqualityComparer<T>.Default;

            for( int i=0; i < nItems; i++ )
            {
                if( comparer.Equals( list[i], item) )
                {
                    return i;
                }
            }

            return -1;
        }

        public static int IndexOf<T>(this IList<T> list, T item)
        {
            if (list is null) throw new ArgumentNullException(nameof(list));

            if (list is List<T> concreteList)
            {
                return concreteList.IndexOf(item);
            }

            if (list is T[] array)
            {
                return Array.IndexOf(array, item);
            }

            int nItems = list.Count;
            var comparer = EqualityComparer<T>.Default;

            for (int i = 0; i < nItems; i++)
            {
                if (comparer.Equals(list[i], item))
                {
                    return i;
                }
            }

            return -1;
        }

        public static int FindIndex<T>(this ImmutableArray<T> list, Predicate<T> predicate)
        {
            if (predicate is null) throw new ArgumentNullException(nameof(predicate));

            int nItems = list.Length;
            for (int i = 0; i < nItems; i++)
            {
                if (predicate(list[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        public static int FindIndex<T>(this IReadOnlyList<T> list, Predicate<T> predicate )
        {
            if (list is null) throw new ArgumentNullException(nameof(list));
            if (predicate is null) throw new ArgumentNullException(nameof(predicate));

            if (list is List<T> concreteList)
            {
                return concreteList.FindIndex(predicate);
            }

            if (list is T[] array)
            {
                return Array.FindIndex(array, predicate);
            }

            int nItems = list.Count;

            for (int i = 0; i < nItems; i++)
            {
                if (predicate( list[i] ))
                {
                    return i;
                }
            }

            return -1;
        }

        public static int FindIndex<T>(this IList<T> list, Predicate<T> predicate)
        {
            if (list is null) throw new ArgumentNullException(nameof(list));
            if (predicate is null) throw new ArgumentNullException(nameof(predicate));

            if (list is List<T> concreteList)
            {
                return concreteList.FindIndex(predicate);
            }

            if (list is T[] array)
            {
                return Array.FindIndex(array, predicate);
            }

            int nItems = list.Count;
            var comparer = EqualityComparer<T>.Default;

            for (int i = 0; i < nItems; i++)
            {
                if (predicate(list[i]))
                {
                    return i;
                }
            }

            return -1;
        }
        private sealed class ReadOnlyWrapper<T> : IReadOnlyList<T>
        {
            private readonly IList<T> source;

            public int Count { get { return this.source.Count; } }
            public T this[int index] { get { return this.source[index]; } }

            public ReadOnlyWrapper(IList<T> source) { this.source = source; }

            public IEnumerator<T> GetEnumerator() { return this.source.GetEnumerator(); }

            IEnumerator IEnumerable.GetEnumerator() { return this.GetEnumerator(); }
        }
    }
}
