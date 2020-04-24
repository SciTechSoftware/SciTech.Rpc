using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SciTech.Collections.Immutable
{
    /// <summary>
    /// Implementation of <see cref="IImmutableList{T}"/> that uses
    /// an array as the underlying storage. This is similar to the <see cref="ImmutableArray{T}"/> 
    /// implementation, but this type is a class rather than a struct.
    /// <para>This type is intended to be used when the list data changes infrequently but
    /// it is not suitable to use a struct, e.g. when the list should be passed around as an <see cref="IImmutableList{T}"/>.
    /// it</para>
    /// <para><b>NOTE!</b> Updates to the list have a time complexity of O(n). If the list is modified
    /// frequently, use <see cref="ImmutableList{T}"</para> instead. If the <see cref="IImmutableList{T}"
    /// abstraction is not necessary, use the <see cref="ImmutableArray{T}"/> type instead.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix")]
    public class ImmutableArrayList<T> : IImmutableList<T>
    {
        public static readonly ImmutableArrayList<T> Empty = new ImmutableArrayList<T>(ImmutableArray<T>.Empty);

        private ImmutableArray<T> data;

        internal ImmutableArrayList(ImmutableArray<T> data)
        {
            this.data = data;
        }

        public T this[int index] => data [index];

        public int Count => this.data.Length;

        public IImmutableList<T> Add(T value) => new ImmutableArrayList<T>(this.data.Add(value));

        public IImmutableList<T> AddRange(IEnumerable<T> items) => new ImmutableArrayList<T>(this.data.AddRange(items));
        
        public IImmutableList<T> Clear() => new ImmutableArrayList<T>(this.data.Clear());

        Enumerator GetEnumerator() => new Enumerator(this.data.GetEnumerator());

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1033:Interface methods should be callable by child types")]
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IImmutableList<T>)this.data).GetEnumerator();

        public int IndexOf(T item, int index, int count, IEqualityComparer<T> equalityComparer)
            => this.data.IndexOf(item, index, count, equalityComparer);


        public IImmutableList<T> Insert(int index, T element) => new ImmutableArrayList<T>(this.data.Insert(index, element));

        public IImmutableList<T> InsertRange(int index, IEnumerable<T> items) => new ImmutableArrayList<T>(this.data.InsertRange(index, items));

        public int LastIndexOf(T item, int index, int count, IEqualityComparer<T> equalityComparer)
            => this.data.LastIndexOf(item, index, count, equalityComparer);

        public IImmutableList<T> Remove(T value, IEqualityComparer<T> equalityComparer)
            => new ImmutableArrayList<T>(this.data.Remove(value, equalityComparer));


        public IImmutableList<T> RemoveAll(Predicate<T> match)
            => new ImmutableArrayList<T>(this.data.RemoveAll(match));

        public IImmutableList<T> RemoveAt(int index)
            => new ImmutableArrayList<T>(this.data.RemoveAt(index));

        public IImmutableList<T> RemoveRange(IEnumerable<T> items, IEqualityComparer<T> equalityComparer)
            => new ImmutableArrayList<T>(this.data.RemoveRange(items, equalityComparer));

        public IImmutableList<T> RemoveRange(int index, int count)
            => new ImmutableArrayList<T>(this.data.RemoveRange(index, count));

        public IImmutableList<T> Replace(T oldValue, T newValue, IEqualityComparer<T> equalityComparer)
            => new ImmutableArrayList<T>(this.data.Replace(oldValue, newValue, equalityComparer));

        public IImmutableList<T> SetItem(int index, T value)
            => new ImmutableArrayList<T>(this.data.SetItem(index, value));


        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1033:Interface methods should be callable by child types")]
        IEnumerator IEnumerable.GetEnumerator()
            => ((IEnumerable)this.data).GetEnumerator();

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034:Nested types should not be visible")]
        public struct Enumerator 
        {
            private readonly ImmutableArray<T>.Enumerator enumerator;

            public Enumerator(ImmutableArray<T>.Enumerator enumerator)
            {
                this.enumerator= enumerator;
            }

            public T Current => this.enumerator.Current;


            public bool MoveNext() => this.enumerator.MoveNext();
        }
    }

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

}
