using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SciTech.Collections
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix")]
    public struct CompactSet<T> : ICollection<T>, IReadOnlyCollection<T>
    {
        private static readonly T[] EmptyArray = Array.Empty<T>();

        private static readonly SmallCollection<T>.CollectionCreator<HashSet<T>> FullSetCreator = (SmallCollection<T> smallCollection, int index, T newItem) =>
        {
            var set = new HashSet<T>(smallCollection)
            {
                newItem
            };
            return set;
        }!;

        private static readonly EqualityComparer<T> Comparer = SmallCollection<T>.Comparer;

        private object? data;

        public CompactSet(T item)
        {
            if (item != null)
            {
                this.data = item;
            }
            else
            {
                this.data = SmallCollection<T>.NullItem;
            }
        }

        public CompactSet(T item1, T item2)
        {
            if (!Comparer.Equals(item1, item2))
            {
                this.data = SmallCollection<T>.Create(item1, item2);
            }
            else
            {
                if (item1 != null)
                {
                    this.data = item1;
                }
                else
                {
                    this.data = SmallCollection<T>.NullItem;
                }
            }
        }


        private CompactSet(object data)
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

                if (this.data is HashSet<T> hashSet)
                {
                    return hashSet.Count;
                }

                Debug.Assert(this.data is T);
                return 1;
            }
        }

        public bool IsReadOnly
        {
            get { return false; }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000:Do not declare static members on generic types")]
        public static CompactSet<T> Unbox(object boxedCompactSet)
        {
            return new CompactSet<T>(boxedCompactSet);
        }

        public bool Add(T item)
        {
            if (this.data == null)
            {
                if (item != null)
                {
                    this.data = item;
                }
                else
                {
                    this.data = SmallCollection<T>.NullItem;
                }

                return true;
            }

            if (this.data is SmallCollection<T> shortList)
            {
                if (shortList.AddToSet(item, FullSetCreator, out object newSet))
                {
                    this.data = newSet;
                    return true;
                }

                return false;
            }

            if (this.data is HashSet<T> set)
            {
                return set.Add(item);
            }

            Debug.Assert(this.data is T);

            T singleItem = (T)this.data;
            if (!Comparer.Equals(item, singleItem))
            {
                this.data = SmallCollection<T>.Create(singleItem, item);
                return true;
            }

            return false;

        }


        public object? Box()
        {
            return this.data;
        }

        public void Clear()
        {
            this.data = null;
        }

        public bool Contains(T item)
        {
            if (this.data == null)
            {
                return false;
            }

            if (this.data is ICollection<T> set )
            {
                return set.Contains(item);
            }

            Debug.Assert(this.data is T);
            return Comparer.Equals((T)this.data, item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array is null) throw new ArgumentNullException(nameof(array));

            if (this.data != null)
            {                
                if (this.data is ICollection<T> set)
                {
                    set.CopyTo(array, arrayIndex);
                } else
                {
                    array[arrayIndex] = (T)this.data;
                }
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            if (this.data == null)
            {
                return ((IList<T>)EmptyArray).GetEnumerator();
            }

            if (this.data is ICollection<T> set )
            {
                return set.GetEnumerator();
            }

            return new SingleEnumerator<T>((T)this.data);
        }

        public bool Remove(T item)
        {
            if (this.data == null)
            {
                return false;
            }


            if (this.data is SmallCollection<T> smallSet)
            {
                if (smallSet.Remove(item, out object? newSet))
                {
                    this.data = newSet;
                    return true;
                }

                return false;
            }

            if (this.data is HashSet<T> set)
            {
                if (set.Remove(item))
                {
                    if (set.Count <= SmallCollection<T>.MaxSize)
                    {
                        this.data = SmallCollection<T>.Create(set);
                    }

                    return true;
                }
            }

            if (Comparer.Equals((T)this.data, item))
            {
                this.data = null;
                return true;
            }

            return false;
        }

        public IReadOnlyCollection<T> AsReadOnlyCollection()
        {
            if (this.data is IReadOnlyCollection<T> list)
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

        void ICollection<T>.Add(T item)
        {
            this.Add(item);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }


    }
}
