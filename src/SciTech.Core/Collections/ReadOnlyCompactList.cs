// #define PUBLIC_UTILITIES
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SciTech.Collections
{
    /// <summary>
    /// A read only wrapper around a <see cref="CompactList{T}"/>. 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types")]
    public readonly struct ReadOnlyCompactList<T> : IReadOnlyList<T>
    {
        public static readonly ReadOnlyCompactList<T> Empty;

        private static readonly T[] EmptyArray = Array.Empty<T>();

        private static readonly EqualityComparer<T> Comparer = SmallCollection<T>.Comparer;

        private readonly object? data;

        public ReadOnlyCompactList(in CompactList<T> other)
        {
            this.data = other.Box();
        }


        internal ReadOnlyCompactList(object data)
        {
            this.data = data;
        }

        public int Count
        {
            get
            {
                if (this.data == null)
                {
                    return 0;
                }

                if (this.data is SmallCollection<T> shortList)
                {
                    return shortList.Count;
                }

                if (this.data is List<T> list)
                {
                    return list.Count;
                }

                Debug.Assert(this.data is T);
                return 1;
            }
        }


        public T this[int index]
        {
            get
            {
                if (this.data is SmallCollection<T> shortList)
                {
                    return shortList[index];
                }
                else if (this.data is List<T> list)
                {
                    return list[index];
                }
                else if (this.data is T singleItem)
                {
                    if (index == 0)
                    {
                        return singleItem;
                    }
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }

        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Argument has no type")]
        public static ReadOnlyCompactList<T> Unbox(object boxedCompactSet)
        {
            return new ReadOnlyCompactList<T>(boxedCompactSet);
        }


        public object? Box()
        {
            return this.data;
        }

        public bool Contains(T item)
        {
            if (this.data == null)
            {
                return false;
            }

            if (this.data is SmallCollection<T> shortList)
            {
                return shortList.Contains(item);
            }

            if (this.data is List<T> list)
            {
                return list.Contains(item);
            }

            return Comparer.Equals((T)this.data, item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array is null) throw new ArgumentNullException(nameof(array));

            if (this.data != null)
            {
                if (this.data is SmallCollection<T> shortList)
                {
                    shortList.CopyTo(array, arrayIndex);
                }
                else if (this.data is List<T> list)
                {
                    list.CopyTo(array, arrayIndex);
                }
                else
                {
                    array[arrayIndex] = (T)this.data;
                }
            }
        }

        public Enumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(this);

        public struct Enumerator : IEnumerator<T>
        {
            private readonly ReadOnlyCompactList<T> compactList;

            private int index;

            internal Enumerator(ReadOnlyCompactList<T> compactList)
            {
                this.compactList = compactList;
                this.index = -1;
            }

            public T Current => this.compactList[index];

            object? IEnumerator.Current => this.compactList[index];

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                this.index++;
                return this.index < this.compactList.Count;
            }

            public void Reset()
            {
                this.index = -1;
            }
        }

        public int IndexOf(T item)
        {
            if (this.data == null)
            {
                return -1;
            }

            if (this.data is SmallCollection<T> shortList)
            {
                return shortList.IndexOf(item);
            }

            if (this.data is List<T> list)
            {
                return list.IndexOf(item);
            }

            return Comparer.Equals((T)this.data, item) ? 0 : -1;
        }

        IEnumerator System.Collections.IEnumerable.GetEnumerator() => new Enumerator(this);

        public IReadOnlyList<T> AsReadOnlyList()
        {
            if (this.data is IReadOnlyList<T> list)
            {
                return list;
            }

            if (this.data == null)
            {
                return EmptyArray;
            }

            // Will cause boxing.
            return this;
        }
    }
}