﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SciTech.Collections.Immutable
{
    public static class ImmutableArrayList
    {
        public static ImmutableArrayList<T> Create<T>()
        {
            return ImmutableArrayList<T>.Empty;
        }

        public static ImmutableArrayList<T> Create<T>(T item)
        {
            return new ImmutableArrayList<T>(ImmutableArray.Create(item));
        }

        public static ImmutableArrayList<T> Create<T>(params T[] items)
        {
            return new ImmutableArrayList<T>(ImmutableArray.Create(items));
        }

        public static ImmutableArrayList<T> CreateRange<T>(IEnumerable<T> items)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));

            if (items is ImmutableArrayList<T> arrayList) return arrayList;

            return new ImmutableArrayList<T>(ImmutableArray.CreateRange(items));
        }
    }

    public static class ImmutableArrayExtensions
    {
        public static ImmutableArrayList<T> ToImmutableArrayList<T>(this IEnumerable<T> list)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));

            return ImmutableArrayList.CreateRange(list);
        }

        public static ImmutableArrayList<T> ToImmutableArrayList<T>(this ImmutableArray<T> immutableArray)
        {
            return new ImmutableArrayList<T>(immutableArray);
        }


        public static ImmutableArrayList<T> ToImmutableArrayList<T>(this ImmutableArray<T>.Builder immutableArrayBuilder)
        {
            if (immutableArrayBuilder is null) throw new System.ArgumentNullException(nameof(immutableArrayBuilder));

            return new ImmutableArrayList<T>(immutableArrayBuilder.ToImmutable());
        }

        public static ImmutableArray<T> TryMoveToImmutable<T>(this ImmutableArray<T>.Builder immutableArrayBuilder)
        {
            if (immutableArrayBuilder is null) throw new ArgumentNullException(nameof(immutableArrayBuilder));

            if (immutableArrayBuilder.Count == immutableArrayBuilder.Capacity)
            {
                return immutableArrayBuilder.MoveToImmutable();
            }

            return immutableArrayBuilder.ToImmutable();
        }

        public static ImmutableArrayList<T> MoveToImmutableArrayList<T>(this ImmutableArray<T>.Builder immutableArrayBuilder)
        {
            if (immutableArrayBuilder is null) throw new ArgumentNullException(nameof(immutableArrayBuilder));

            if (immutableArrayBuilder.Count == immutableArrayBuilder.Capacity)
            {
                return new ImmutableArrayList<T>(immutableArrayBuilder.MoveToImmutable());
            }

            return new ImmutableArrayList<T>(immutableArrayBuilder.ToImmutable());
        }

        public static ImmutableArrayList<T> TryMoveToImmutableArrayList<T>(this ImmutableArray<T>.Builder immutableArrayBuilder)
        {
            if (immutableArrayBuilder is null) throw new ArgumentNullException(nameof(immutableArrayBuilder));

            return new ImmutableArrayList<T>(immutableArrayBuilder.ToImmutable());
        }
    }

    /// <summary>
    /// Implementation of <see cref="IImmutableList{T}"/> that uses
    /// an array as the underlying storage. This is similar to the <see cref="ImmutableArray{T}"/>
    /// implementation, but this type is a class rather than a struct.
    /// <para>This type is intended to be used when the list data changes infrequently but
    /// it is not suitable to use a struct, e.g. when the list should be passed around as an <see cref="IImmutableList{T}"/>.
    /// </para>
    /// <para><b>NOTE!</b> Updates to the list have a time complexity of O(n). If the list is modified
    /// frequently, use <see cref="ImmutableList{T}"/> instead. If the <see cref="IImmutableList{T}"/>
    /// abstraction is not necessary, use the <see cref="ImmutableArray{T}"/> type instead.
    /// </para>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public sealed class ImmutableArrayList<T> : IImmutableList<T>
    {
        public static readonly ImmutableArrayList<T> Empty = new ImmutableArrayList<T>(ImmutableArray<T>.Empty);

        private ImmutableArray<T> data;

        internal ImmutableArrayList(ImmutableArray<T> data)
        {
            this.data = data;
        }

        public int Count => this.data.Length;

        public T this[int index] => data[index];

        public IImmutableList<T> Add(T value) => new ImmutableArrayList<T>(this.data.Add(value));

        public IImmutableList<T> AddRange(IEnumerable<T> items) => new ImmutableArrayList<T>(this.data.AddRange(items));

        public IImmutableList<T> Clear() => new ImmutableArrayList<T>(this.data.Clear());

        public Enumerator GetEnumerator() => new Enumerator(this.data);

        public int IndexOf(T item, int index, int count, IEqualityComparer<T>? equalityComparer)
            => this.data.IndexOf(item, index, count, equalityComparer);

        public IImmutableList<T> Insert(int index, T element) => new ImmutableArrayList<T>(this.data.Insert(index, element));

        public IImmutableList<T> InsertRange(int index, IEnumerable<T> items) => new ImmutableArrayList<T>(this.data.InsertRange(index, items));

        public int LastIndexOf(T item, int index, int count, IEqualityComparer<T>? equalityComparer)
            => this.data.LastIndexOf(item, index, count, equalityComparer);

        public IImmutableList<T> Remove(T value, IEqualityComparer<T>? equalityComparer)
            => new ImmutableArrayList<T>(this.data.Remove(value, equalityComparer));

        public IImmutableList<T> RemoveAll(Predicate<T> match)
            => new ImmutableArrayList<T>(this.data.RemoveAll(match));

        public IImmutableList<T> RemoveAt(int index)
            => new ImmutableArrayList<T>(this.data.RemoveAt(index));

        public IImmutableList<T> RemoveRange(IEnumerable<T> items, IEqualityComparer<T>? equalityComparer)
            => new ImmutableArrayList<T>(this.data.RemoveRange(items, equalityComparer));

        public IImmutableList<T> RemoveRange(int index, int count)
            => new ImmutableArrayList<T>(this.data.RemoveRange(index, count));

        public IImmutableList<T> Replace(T oldValue, T newValue, IEqualityComparer<T>? equalityComparer)
            => new ImmutableArrayList<T>(this.data.Replace(oldValue, newValue, equalityComparer));

        public IImmutableList<T> SetItem(int index, T value)
            => new ImmutableArrayList<T>(this.data.SetItem(index, value));

        public ImmutableArray<T> ToImmutableArray()
        {
            return this.data;
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IImmutableList<T>)this.data).GetEnumerator();

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1033:Interface methods should be callable by child types")]
        IEnumerator IEnumerable.GetEnumerator()
            => ((IEnumerable)this.data).GetEnumerator();

        /// <summary>
        /// An array enumerator.
        /// </summary>
        /// <remarks>
        /// It is important that this enumerator does NOT implement <see cref="IDisposable"/>.
        /// We want the iterator to inline when we do foreach and to not result in
        /// a try/finally frame in the client.
        /// </remarks>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034:Nested types should not be visible")]
        public struct Enumerator
        {
            /// <summary>
            /// The array being enumerated.
            /// </summary>
            private readonly ImmutableArray<T> _array;

            /// <summary>
            /// The currently enumerated position.
            /// </summary>
            /// <value>
            /// -1 before the first call to <see cref="MoveNext"/>.
            /// >= this.array.Length after <see cref="MoveNext"/> returns false.
            /// </value>
            private int _index;

            /// <summary>
            /// Initializes a new instance of the <see cref="Enumerator"/> struct.
            /// </summary>
            /// <param name="array">The array to enumerate.</param>
            internal Enumerator(ImmutableArray<T> array)
            {
                _array = array;
                _index = -1;
            }

            /// <summary>
            /// Gets the currently enumerated value.
            /// </summary>
            public T Current
            {
                get
                {
                    return _array[_index];
                }
            }

            /// <summary>
            /// Advances to the next value to be enumerated.
            /// </summary>
            /// <returns><c>true</c> if another item exists in the array; <c>false</c> otherwise.</returns>
            public bool MoveNext()
            {
                return ++_index < _array.Length;
            }
        }
    }
}