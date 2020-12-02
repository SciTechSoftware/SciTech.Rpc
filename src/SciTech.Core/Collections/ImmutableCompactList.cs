using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SciTech.Collections
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types")]
    public readonly struct ImmutableCompactList<T> : IReadOnlyList<T>
    {
        public static readonly ImmutableCompactList<T> Empty = new ImmutableCompactList<T>();

        private static readonly T[] EmptyArray = Array.Empty<T>();

        private static readonly SmallCollection<T>.CollectionCreator<T[]> FullListCreator = (SmallCollection<T> smallCollection, int index, T newItem) =>
        {
            var list = new T[smallCollection.Count + 1];
            int si = 0; 
            for( int i=0; i < list.Length; i++ )
            {
                if (i != index)
                {
                    list[i] = smallCollection[si++];
                }
            }

            list[index] = newItem;
            return list;
        };


        private static readonly EqualityComparer<T> Comparer = SmallCollection<T>.Comparer;

        private readonly object? data;

        public ImmutableCompactList(T value)
        {
            if (value != null)
            {
                this.data = value;
            } else
            {
                this.data = SmallCollection<T>.NullItem;
            }
        }

        public ImmutableCompactList(params T[] value)
        {
            this.data = CreateListData(value);
        }

        public ImmutableCompactList(IEnumerable<T> collection) 
        {
            this.data = CreateListData(collection);
        }

        public ImmutableCompactList(ImmutableCompactList<T> other) 
        {
            this.data = other.data;
        }

        private ImmutableCompactList(object data)
        {
            this.data = data;
        }

        public ImmutableCompactList<T> Add(T item)
        {
            if (this.data == null)
            {
                if (item != null)
                {
                    return new ImmutableCompactList<T>(item);
                }
                else
                {
                    return new ImmutableCompactList<T>(SmallCollection<T>.NullItem);
                }
            }
            else if (this.data is SmallCollection<T> shortList)
            {
                return new ImmutableCompactList<T>(shortList.Add(item, FullListCreator));
            }
            else if (this.data is T[] list)
            {
                // TODO: Switch to ImmutableList after some other threshold. 
                // This can become very slow if a lot of items are added.
                Array.Resize(ref list, list.Length + 1);
                list[list.Length - 1] = item;
                return new ImmutableCompactList<T>(list);
            }
            else
            {
                Debug.Assert(this.data is T);

                return new ImmutableCompactList<T>(SmallCollection<T>.Create((T)this.data, item));
            }
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

                if (this.data is T[] list)
                {
                    return list.Length;
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
                else if (this.data is T[] list)
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

        public static ImmutableCompactList<T> FromArray(T[] array)
        {
            if (array != null)
            {
                return new ImmutableCompactList<T>(array);
            }

            return ImmutableCompactList<T>.Empty;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000:Do not declare static members on generic types")]
        public static ImmutableCompactList<T> Unbox(object boxedCompactSet)
        {
            return new ImmutableCompactList<T>(boxedCompactSet);
        }

        internal static object? CreateListData(IEnumerable<T> enumerable)
        {
            if (!(enumerable is ICollection<T> collection))
            {
                // Copy items into a temporary collection, to avoid 
                // enumerating the enumerator twice.
                collection = new List<T>(enumerable);
            }

            int totalCount = collection.Count;
            if (totalCount <= SmallCollection<T>.MaxSize)
            {
                return SmallCollection<T>.Create(collection);
            }
            else 
            {
                T[] array = new T[totalCount];
                collection.CopyTo(array, 0);
                return array;
            }
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

            if (this.data is T[] list)
            {
                return Array.IndexOf(list, item) >= 0;
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
                else if (this.data is T[] list)
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

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            if (this.data == null)
            {
                return ((IList<T>)EmptyArray).GetEnumerator();
            }

            if (this.data is SmallCollection<T> shortSet)
            {
                return shortSet.GetEnumerator();
            }

            if (this.data is T[] list)
            {
                return ((IEnumerable<T>)list).GetEnumerator();
            }

            return new SingleEnumerator<T>((T)this.data);
        }

        public struct Enumerator 
        {
            private readonly ImmutableCompactList<T> compactList;

            int index;

            internal Enumerator(ImmutableCompactList<T> compactList)
            {
                this.compactList = compactList;
                this.index = -1;
            }

            public T Current => this.compactList[index];
           
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

            if (this.data is T[] list)
            {
                return Array.IndexOf(list, item);
            }

            return Comparer.Equals((T)this.data, item) ? 0 : -1;
        }

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();

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
