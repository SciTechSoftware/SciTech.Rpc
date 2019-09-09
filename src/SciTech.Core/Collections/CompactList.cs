using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SciTech.Collections
{

    public static class CompactList
    {
        public static CompactList<T> FromArray<T>(T[] array)
        {
            if (array != null)
            {
                return new CompactList<T>(array);
            }

            return CompactList<T>.Empty;
        }


        public static CompactList<T> Unbox<T>(object boxedCompacList)
        {
            return new CompactList<T>(boxedCompacList);
        }
    }

#pragma warning disable CA1710 // Identifiers should have correct suffix
#pragma warning disable CA1815 // Override equals and operator equals on value types

    /// <summary>
    /// Provides a list implementation that is intended for very short lists. It is very compact 
    /// and allocation free for list of zero and one items. Storage is optimized for lists up to
    /// the size of 4, after that it will fallback to use a standard <see cref="List{T}"/>.
    /// </summary>
    /// <remarks>
    /// It is recommended that this list is used as an array, i.e. with a fixed size. Modifying the 
    /// list with <see cref="Add"/>, <see cref="Remove"/> and similar methods is inefficient, since that
    /// backing storage has to be re-allocated when the size changes (for list of size 4 and less).
    /// </remarks>
    /// <typeparam name="T"></typeparam>
    public struct CompactList<T> : IList<T>, IReadOnlyList<T>
    {
        public static readonly CompactList<T> Empty = new CompactList<T>();

        private static readonly T[] EmptyArray = Array.Empty<T>();

        private static readonly SmallCollection<T>.CollectionCreator<List<T>> FullListCreator = () => new List<T>();

        private static readonly EqualityComparer<T> Comparer = SmallCollection<T>.Comparer;

        private object? data;

        public CompactList(IEnumerable<T> collection) : this()
        {
            this.AddRange(collection);
        }

        public CompactList(CompactList<T> other ) : this()
        {
            if (other.data is SmallCollection<T> smallCollection)
            {
                this.data = smallCollection.Clone();
            }
            else if (other.data is List<T> otherList)
            {
                this.data = new List<T>(otherList);
            } else 
            {
                Debug.Assert(other.data is T || other.data == null);
                this.data = other.data;
            }
        }

        internal CompactList(object data)
        {
            this.data = data;
        }

        public bool IsEmpty => this.data == null;
        
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

                if( this.data is List<T> list)
                {
                    return list.Count;
                }

                Debug.Assert(this.data is T);
                return 1;
            }
        }

        public bool IsReadOnly
        {
            get { return false; }
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
                } else if (this.data is T singleItem && index == 0 )
                {
                    return singleItem;
                }

#pragma warning disable CA1065 // Do not raise exceptions in unexpected locations
                throw new ArgumentOutOfRangeException(nameof(index));
#pragma warning restore CA1065 // Do not raise exceptions in unexpected locations
            }
            set
            {
                if (this.data is SmallCollection<T> shortList)
                {
                    this.data = shortList.SetAt(index, value);
                }
                else if (this.data is List<T> list)
                {
                    list[index] = value;
                }
                else if (this.data is T singleItem && index == 0)
                {
                    if (value == null)
                    {
                        this.data = SmallCollection<T>.NullItem;
                    }
                    else
                    {
                        this.data = value;
                    }
                }
                else
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }




        public void Add(T item)
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
            }            
            else if (this.data is SmallCollection<T> shortList)
            {
                this.data = shortList.Add(item, FullListCreator);
            }
            else if( this.data is List<T> list )
            {
                list.Add(item);
            } else
            {
                Debug.Assert(this.data is T);

                this.data = SmallCollection<T>.Create((T)this.data, item);
            }
        }

        public void Reset(int size)
        {
            if (size <= SmallCollection<T>.MaxSize )
            {
                this.data = SmallCollection<T>.Create(size);
                return;
            }

            var list = new List<T>(size);
            for(int i=0; i < size; i++)
            {
                list.Add(default!);
            }
        }

        public void AddRange(IEnumerable<T> enumerable)
        {
            if (enumerable is null) throw new ArgumentNullException(nameof(enumerable));

            if (enumerable is ICollection<T> collection)
            {
                int totalCount = this.Count + collection.Count;
                if (totalCount <= SmallCollection<T>.MaxSize && this.data == null)
                {
                    this.data = SmallCollection<T>.Create(collection);
                    return;
                }
                else if (totalCount > SmallCollection<T>.MaxSize)
                {
                    if (this.data is List<T> list)
                    {
                        if (list.Capacity < totalCount)
                        {
                            list.Capacity = totalCount;
                        }
                    }
                    else
                    {
                        list = new List<T>();
                        if (list.Capacity < totalCount)
                        {
                            list.Capacity = totalCount;
                        }
                        
                        if (this.data is SmallCollection<T> shortList)
                        {
                            list.AddRange(shortList);
                        } else if (this.data is T singleItem)
                        {
                            list.Add(singleItem);
                        }
                    }

                    list.AddRange(collection);

                    this.data = list;

                    return;
                }
            }

            foreach (var item in enumerable)
            {
                this.Add(item);
            }
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

            if (this.data is SmallCollection<T> shortList)
            {
                return shortList.Contains(item);
            }

            if( this.data is List<T> list )
            {
                return list.Contains(item);
            }

            return Comparer.Equals((T)this.data, item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (this.data != null)
            {
                if (array is null) throw new ArgumentNullException(nameof(array));

                if (this.data is SmallCollection<T> shortList)
                {
                    shortList.CopyTo(array, arrayIndex);
                }
                else if( this.data is List<T> list )
                {
                    list.CopyTo(array, arrayIndex);
                } else{

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

            if (this.data is SmallCollection<T> shortSet)
            {
                return shortSet.GetEnumerator();
            }

            if( this.data is List<T> list )
            {
                return list.GetEnumerator();
            }

            return new SingleEnumerator<T>((T)this.data);
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

        public void Insert(int index, T item)
        {
            if (this.data == null)
            {
                if (index == 0)
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
            } else if (this.data is SmallCollection<T> shortList)
            {
                this.data = shortList.Insert(index, item, FullListCreator);
            }
            else if (this.data is List<T> list)
            {
                list.Insert(index, item);
            }
            else
            {
                if (index == 0)
                {
                    this.data = SmallCollection<T>.Create(item, (T)this.data);
                }
                else if (index == 1)
                {
                    this.data = SmallCollection<T>.Create((T)this.data, item);
                }
            }

            throw new ArgumentOutOfRangeException(nameof(index));
        }

        public bool Remove(T item)
        {
            if (this.data == null)
            {
                return false;
            }


            if (this.data is SmallCollection<T> shortList)
            {
                if (shortList.Remove(item, out object? newSet))
                {
                    this.data = newSet;
                    return true;
                }

                return false;
            }

            if (this.data is List<T> list)
            {
                if (list.Remove(item))
                {
                    if (list.Count <= SmallCollection<T>.MaxSize)
                    {
                        this.data = SmallCollection<T>.Create(list);
                    }

                    return true;
                }

                return false;
            }

            if (Comparer.Equals((T)this.data, item))
            {
                this.data = null;
                return true;
            }

            return false;
        }

        public void RemoveAt(int index)
        {
            if (this.data == null)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (this.data is SmallCollection<T> shortList)
            {
                this.data = shortList.RemoveAt(index);
            }
            else if (this.data is List<T> list)
            {
                list.RemoveAt(index);
                if (list.Count <= SmallCollection<T>.MaxSize)
                {
                    this.data = SmallCollection<T>.Create(list);
                }
            }
            else
            {
                if (index == 0)
                {
                    this.data = null;
                }
                else
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }

        internal void RemoveRange(int index, int count)
        {
            if (this.data == null)
            {
                if (index != 0 || count != 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return;
            }

            if (this.data is SmallCollection<T> shortList)
            {
                if (index < 0 || index + count > shortList.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                for (int removeIndex = index + count - 1; removeIndex >= index; removeIndex--)
                {
                    this.RemoveAt(removeIndex);
                }
            }
            else if (this.data is List<T> list)
            {
                list.RemoveRange(index, count);
                if (list.Count <= SmallCollection<T>.MaxSize)
                {
                    this.data = SmallCollection<T>.Create(list);
                }
            }
            else
            {
                if (index == 0)
                {
                    if (count == 1)
                    {
                        this.data = null;
                        return;
                    }
                    else if (count == 0)
                    {
                        return;
                    }
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }

        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

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
#pragma warning restore CA1815 // Override equals and operator equals on value types
#pragma warning restore CA1710 // Identifiers should have correct suffix
}
