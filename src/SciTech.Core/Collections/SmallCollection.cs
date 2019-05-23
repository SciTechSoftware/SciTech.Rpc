using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace SciTech.Collections
{
    internal abstract class SmallCollection<T> : ICollection<T>, IReadOnlyList<T>
    {
        internal const int MaxSize = 4;

        internal static readonly EqualityComparer<T> Comparer = EqualityComparer<T>.Default;

        internal static readonly SmallCollection<T> NullItem = new NullItemCollection();

        internal delegate TCollection CollectionCreator<out TCollection>() where TCollection : ICollection<T>;

        public abstract int Count { get; }

        /// <summary>
        /// Cannot modify SmallCollection through ICollection interface
        /// </summary>
        bool ICollection<T>.IsReadOnly => true;

        public abstract T this[int index] { get; }

        public abstract SmallCollection<T> Clone();

        public virtual bool Contains(T item) => this.IndexOf(item) >= 0;

        public abstract void CopyTo(T[] array, int arrayIndex);

        public abstract IEnumerator<T> GetEnumerator();

        public abstract int IndexOf(T item);

        internal static object Create(int size)
        {
            switch (size)
            {
                case 0:
                    return null;
                case 1:
                    return NullItem;
                case 2:
                    return new TwoItemCollection();
                case 3:
                    return new ThreeItemCollection();
                case 4:
                    return new FourItemCollection();
            }

            throw new ArgumentOutOfRangeException(nameof(size));
        }

        internal static object Create(ICollection<T> collection)
        {
            if (collection is IList<T> list)
            {
                return Create(list);
            }

            int count = collection.Count;
            Debug.Assert(count <= MaxSize);

            if (count == 0)
            {
                return null;
            }

            var enumerator = collection.GetEnumerator();
            enumerator.MoveNext();
            var value_0 = enumerator.Current;

            if (count == 1)
            {
                if (value_0 == null)
                {
                    return NullItem;
                }

                return value_0;
            }

            enumerator.MoveNext();
            var value_1 = enumerator.Current;
            if (count == 2)
            {
                return new TwoItemCollection(value_0, value_1);
            }

            enumerator.MoveNext();
            var value_2 = enumerator.Current;
            if (count == 3)
            {
                return new ThreeItemCollection(value_0, value_1, value_2);
            }

            enumerator.MoveNext();
            var value_3 = enumerator.Current;
            Debug.Assert(count == 4);
            return new FourItemCollection(value_0, value_1, value_2, value_3);
            //}

            //enumerator.MoveNext();
            //var value_4 = enumerator.Current;
            //if (count == 5)
            //{
            //    return new SixItemCollection(value_0, value_1, value_2, value_3, value_4);
            //}

            //enumerator.MoveNext();
            //var value_5 = enumerator.Current;
            //return new SixItemCollection(value_0, value_1, value_2, value_3, value_4, value_5);
        }

        internal static object Create(IList<T> list)
        {
            int count = list.Count;
            Debug.Assert(count <= MaxSize);

            if (count == 0)
            {
                return null;
            }

            var value_0 = list[0];

            if (count == 1)
            {
                if (value_0 == null)
                {
                    return NullItem;
                }

                return value_0;
            }

            if (count == 2)
            {
                return new TwoItemCollection(value_0, list[1]);
            }

            if (count == 3)
            {
                return new ThreeItemCollection(value_0, list[1], list[2]);
            }

            Debug.Assert(count == 4);
            return new FourItemCollection(value_0, list[1], list[2], list[3]);
            //}

            //enumerator.MoveNext();
            //var value_4 = enumerator.Current;
            //if (count == 5)
            //{
            //    return new SixItemCollection(value_0, value_1, value_2, value_3, value_4);
            //}

            //enumerator.MoveNext();
            //var value_5 = enumerator.Current;
            //return new SixItemCollection(value_0, value_1, value_2, value_3, value_4, value_5);
        }

        internal static SmallCollection<T> Create(T value_0, T value_1)
        {
            return new TwoItemCollection(value_0, value_1);
        }

        internal abstract object Add(T item, CollectionCreator<ICollection<T>> fullSetCreator);

        internal virtual bool AddToSet(T item, CollectionCreator<ICollection<T>> fullSetCreator, out object newSet)
        {
            if (this.IndexOf(item) < 0)
            {
                newSet = this.Add(item, fullSetCreator);
                return true;
            }

            newSet = this;
            return false;
        }

        internal abstract object Insert(int index, T item, CollectionCreator<IList<T>> fullSetCreator);

        internal virtual bool Remove(T item, out object newSet)
        {
            int index = this.IndexOf(item);
            if (index >= 0)
            {
                newSet = this.RemoveAt(index);
                return true;
            }

            newSet = this;
            return false;
        }

        /// <summary>
        /// </summary>
        /// <param name="index"></param>
        /// <exception cref="ArgumentOutOfRangeException"/>
        /// <returns></returns>
        internal abstract object RemoveAt(int index);

        internal abstract object SetAt(int index, T item);

        internal abstract bool TryGet(T item, out T existingItem);

        void ICollection<T>.Add(T item)
        {
            throw new NotSupportedException();
        }

        void ICollection<T>.Clear()
        {
            throw new NotSupportedException();
        }

        IEnumerator IEnumerable.GetEnumerator() { return this.GetEnumerator(); }

        bool ICollection<T>.Remove(T item)
        {
            throw new NotSupportedException();
        }

        private sealed class FourItemCollection : SmallCollection<T>
        {
            private T value_0;

            private T value_1;

            private T value_2;

            private T value_3;
            internal FourItemCollection()
            {

            }

            internal FourItemCollection(T value_0, T value_1, T value_2, T value_3)
            {
                this.value_0 = value_0;
                this.value_1 = value_1;
                this.value_2 = value_2;
                this.value_3 = value_3;
            }

            public override int Count
            {
                get { return 4; }
            }

            public override T this[int index]
            {
                get
                {
                    switch (index)
                    {
                        case 0:
                            return this.value_0;
                        case 1:
                            return this.value_1;
                        case 2:
                            return this.value_2;
                        case 3:
                            return this.value_3;
                    }

                    throw new ArgumentOutOfRangeException(nameof(index));
                }
            }

            public override SmallCollection<T> Clone()
            {
                return new FourItemCollection(this.value_0, this.value_1, this.value_2, this.value_3);
            }

            public override void CopyTo(T[] array, int arrayIndex)
            {
                array[arrayIndex] = this.value_0;
                array[arrayIndex + 1] = this.value_1;
                array[arrayIndex + 2] = this.value_2;
                array[arrayIndex + 3] = this.value_3;
            }

            public override IEnumerator<T> GetEnumerator()
            {
                yield return this.value_0;
                yield return this.value_1;
                yield return this.value_2;
                yield return this.value_3;
            }

            public override int IndexOf(T item)
            {
                if (Comparer.Equals(this.value_0, item))
                {
                    return 0;
                }

                if (Comparer.Equals(this.value_1, item))
                {
                    return 1;
                }

                if (Comparer.Equals(this.value_2, item))
                {
                    return 2;
                }

                if (Comparer.Equals(this.value_3, item))
                {
                    return 3;
                }

                return -1;
            }

            internal override object Add(T item, CollectionCreator<ICollection<T>> fullListCreator)
            {
                ICollection<T> list = this.CreateFullCollection(fullListCreator);
                list.Add(item);
                return list;
            }

            internal override object Insert(int index, T item, CollectionCreator<IList<T>> fullListCreator)
            {
                var list = fullListCreator();
                switch (index)
                {
                    case 0:
                        list[0] = item;
                        list[1] = this.value_0;
                        list[2] = this.value_1;
                        list[3] = this.value_2;
                        list[4] = this.value_3;
                        break;
                    case 1:
                        list[0] = this.value_0;
                        list[1] = item;
                        list[2] = this.value_1;
                        list[3] = this.value_2;
                        list[4] = this.value_3;
                        break;
                    case 2:
                        list[0] = this.value_0;
                        list[1] = this.value_1;
                        list[2] = item;
                        list[3] = this.value_2;
                        list[4] = this.value_3;
                        break;
                    case 3:
                        list[0] = this.value_0;
                        list[1] = this.value_1;
                        list[2] = this.value_2;
                        list[3] = item;
                        list[4] = this.value_3;
                        break;
                    case 4:
                        list[0] = this.value_0;
                        list[1] = this.value_1;
                        list[2] = this.value_2;
                        list[3] = this.value_3;
                        list[4] = item;
                        break;
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }

            internal override object RemoveAt(int index)
            {
                switch (index)
                {
                    case 0:
                        return new ThreeItemCollection(this.value_1, this.value_2, this.value_3);
                    case 1:
                        return new ThreeItemCollection(this.value_0, this.value_2, this.value_3);
                    case 2:
                        return new ThreeItemCollection(this.value_0, this.value_1, this.value_3);
                    case 3:
                        return new ThreeItemCollection(this.value_0, this.value_1, this.value_2);
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }

            internal override object SetAt(int index, T item)
            {
                switch (index)
                {
                    case 0:
                        this.value_0 = item;
                        break;
                    case 1:
                        this.value_1 = item;
                        break;
                    case 2:
                        this.value_2 = item;
                        break;
                    case 3:
                        this.value_3 = item;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(index));
                }

                return this;
            }

            internal override bool TryGet(T item, out T existingItem)
            {
                if (Comparer.Equals(this.value_0, item))
                {
                    existingItem = this.value_0;
                    return true;
                }

                if (Comparer.Equals(this.value_1, item))
                {
                    existingItem = this.value_1;
                    return true;
                }

                if (Comparer.Equals(this.value_2, item))
                {
                    existingItem = this.value_2;
                    return true;
                }

                if (Comparer.Equals(this.value_3, item))
                {
                    existingItem = this.value_3;
                    return true;
                }

                existingItem = default;
                return false;
            }

            private ICollection<T> CreateFullCollection(CollectionCreator<ICollection<T>> fullListCreator)
            {
                var list = fullListCreator();
                list.Add(this.value_0);
                list.Add(this.value_1);
                list.Add(this.value_2);
                list.Add(this.value_3);
                return list;
            }
        }

        private sealed class NullItemCollection : SmallCollection<T>
        {
            public override int Count
            {
                get { return 1; }
            }

            public override T this[int index]
            {
                get
                {
                    if (index == 0)
                    {
                        return default;
                    }

                    throw new ArgumentOutOfRangeException(nameof(index));
                }
            }

            public override SmallCollection<T> Clone() => this;

            public override bool Contains(T item)
            {
                return item == null;
            }

            public override void CopyTo(T[] array, int arrayIndex)
            {
                array[arrayIndex] = default;
            }

            public override IEnumerator<T> GetEnumerator()
            {
                return new SingleEnumerator<T>(default);
            }

            public override int IndexOf(T item)
            {
                return (item == null) ? 0 : -1;
            }

            internal override object Add(T item, CollectionCreator<ICollection<T>> fullSetCreator)
            {
                return new TwoItemCollection(default, item);
            }

            internal override bool AddToSet(T item, CollectionCreator<ICollection<T>> fullSetCreator, out object newSet)
            {
                if (item != null)
                {
                    newSet = new TwoItemCollection(default, item);
                    return true;
                }

                newSet = this;
                return false;
            }

            internal override object Insert(int index, T item, CollectionCreator<IList<T>> fullSetCreator)
            {
                switch (index)
                {
                    case 0:
                        return new TwoItemCollection(item, default);
                    case 1:
                        return new TwoItemCollection(default, item);
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }

            internal override bool Remove(T item, out object newSet)
            {
                if (item == null)
                {
                    newSet = null;
                    return true;
                }

                newSet = this;
                return false;
            }

            internal override object RemoveAt(int index)
            {
                if (index == 0)
                {
                    return null;
                }
                else
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }
            }

            internal override object SetAt(int index, T item)
            {
                if (index == 0)
                {
                    if (item == null)
                    {
                        // 
                        return this;
                    }

                    return item;
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }

            internal override bool TryGet(T item, out T existingItem)
            {
                existingItem = default;
                return item == null;
            }
        }

        //private sealed class SixItemCollection : SmallCollection<T>
        //{
        //    private int count;
        //    private T value_0;
        //    private T value_1;
        //    private T value_2;
        //    private T value_3;
        //    private T value_4;
        //    private T value_5;
        //    internal SixItemCollection(T value_0, T value_1, T value_2, T value_3)
        //    {
        //        this.count = 4;
        //        this.value_0 = value_0;
        //        this.value_1 = value_1;
        //        this.value_2 = value_2;
        //        this.value_3 = value_3;
        //    }
        //    internal SixItemCollection(T value_0, T value_1, T value_2, T value_3, T value_4)
        //    {
        //        this.count = 5;
        //        this.value_0 = value_0;
        //        this.value_1 = value_1;
        //        this.value_2 = value_2;
        //        this.value_3 = value_3;
        //        this.value_4 = value_4;
        //    }
        //    internal SixItemCollection(T value_0, T value_1, T value_2, T value_3, T value_4, T value_5)
        //    {
        //        this.count = 6;
        //        this.value_0 = value_0;
        //        this.value_1 = value_1;
        //        this.value_2 = value_2;
        //        this.value_3 = value_3;
        //        this.value_4 = value_4;
        //        this.value_5 = value_5;
        //    }
        //    public override int Count
        //    {
        //        get { return this.count; }
        //    }
        //    public override T this[int index]
        //    {
        //        get
        //        {
        //            switch (index)
        //            {
        //                case 0:
        //                    return this.value_0;
        //                case 1:
        //                    return this.value_1;
        //                case 2:
        //                    return this.value_2;
        //                case 3:
        //                    return this.value_3;
        //                case 4:
        //                    if (this.count > 4)
        //                    {
        //                        return this.value_4;
        //                    }
        //                    break;
        //                case 5:
        //                    if (this.count > 5)
        //                    {
        //                        return this.value_5;
        //                    }
        //                    break;
        //            }
        //            throw new ArgumentOutOfRangeException();
        //        }
        //    }
        //    public override IEnumerator<T> GetEnumerator()
        //    {
        //        yield return this.value_0;
        //        yield return this.value_1;
        //        yield return this.value_2;
        //        yield return this.value_3;
        //        if (this.count > 4)
        //        {
        //            yield return this.value_4;
        //        }
        //        if (this.count > 5)
        //        {
        //            yield return this.value_5;
        //        }
        //    }
        //    internal override object Add(T item, CollectionCreator<ICollection<T>> fullListCreator)
        //    {
        //        Debug.Assert(this.count >= 4 && this.count <= 6);
        //        switch (this.count)
        //        {
        //            case 4:
        //                this.value_4 = item;
        //                break;
        //            case 5:
        //                this.value_5 = item;
        //                break;
        //            default:
        //                {
        //                    ICollection<T> list = this.CreateFullCollection(fullListCreator);
        //                    list.Add(item);
        //                    return list;
        //                }
        //        }
        //        this.count++;
        //        return this;
        //    }
        //    internal override void CopyTo(T[] array, int arrayIndex)
        //    {
        //        array[arrayIndex] = this.value_0;
        //        array[arrayIndex + 1] = this.value_1;
        //        array[arrayIndex + 2] = this.value_2;
        //        array[arrayIndex + 3] = this.value_3;
        //        if (this.count > 4)
        //        {
        //            array[arrayIndex + 4] = this.value_4;
        //        }
        //        if (this.count > 5)
        //        {
        //            array[arrayIndex + 5] = this.value_5;
        //        }
        //    }
        //    internal override int IndexOf(T item)
        //    {
        //        if (Comparer.Equals(this.value_0, item))
        //        {
        //            return 0;
        //        }
        //        if (Comparer.Equals(this.value_1, item))
        //        {
        //            return 1;
        //        }
        //        if (Comparer.Equals(this.value_2, item))
        //        {
        //            return 2;
        //        }
        //        if (Comparer.Equals(this.value_3, item))
        //        {
        //            return 3;
        //        }
        //        if (this.count > 4)
        //        {
        //            if (Comparer.Equals(this.value_4, item))
        //            {
        //                return 4;
        //            }
        //            if (this.count > 5)
        //            {
        //                if (Comparer.Equals(this.value_5, item))
        //                {
        //                    return 5;
        //                }
        //            }
        //        }
        //        return -1;
        //    }
        //    internal override object Insert(int index, T item, CollectionCreator<IList<T>> fullListCreator)
        //    {
        //        Debug.Assert(this.count >= 4 && this.count <= 6);
        //        if (index < 0 || index >= this.count)
        //        {
        //            throw new ArgumentOutOfRangeException();
        //        }
        //        if (this.count < 6)
        //        {
        //            switch (index)
        //            {
        //                case 0:
        //                    if (this.count >= 5) this.value_5 = this.value_4;
        //                    if (this.count >= 4) this.value_4 = this.value_3;
        //                    this.value_3 = this.value_2;
        //                    this.value_2 = this.value_1;
        //                    this.value_1 = this.value_0;
        //                    this.value_0 = item;
        //                    break;
        //                case 1:
        //                    if (this.count >= 5) this.value_5 = this.value_4;
        //                    if (this.count >= 4) this.value_4 = this.value_3;
        //                    this.value_3 = this.value_2;
        //                    this.value_2 = this.value_1;
        //                    this.value_1 = item;
        //                    break;
        //                case 2:
        //                    if (this.count >= 5) this.value_5 = this.value_4;
        //                    if (this.count >= 4) this.value_4 = this.value_3;
        //                    this.value_3 = this.value_2;
        //                    this.value_2 = item;
        //                    break;
        //                case 3:
        //                    if (this.count >= 5) this.value_5 = this.value_4;
        //                    if (this.count >= 4) this.value_4 = this.value_3;
        //                    this.value_3 = item;
        //                    break;
        //                case 4:
        //                    if (this.count >= 5) this.value_5 = this.value_4;
        //                    this.value_4 = item;
        //                    break;
        //                case 5:
        //                    this.value_5 = item;
        //                    break;
        //            }
        //            return this;
        //        }
        //        else
        //        {
        //            var list = (IList<T>)this.CreateFullCollection(fullListCreator);
        //            list.Insert(index, item);
        //            return list;
        //        }
        //    }
        //    internal override object RemoveAt(int index)
        //    {
        //        Debug.Assert(this.count >= 4 && this.count <= 6);
        //        if (index < 0 || index >= this.count)
        //        {
        //            throw new ArgumentOutOfRangeException();
        //        }
        //        switch (index)
        //        {
        //            case 0:
        //                this.value_0 = this.value_1;
        //                goto case 1;
        //            case 1:
        //                this.value_1 = this.value_2;
        //                goto case 2;
        //            case 2:
        //                this.value_2 = this.value_3;
        //                goto case 3;
        //            case 3:
        //                this.value_3 = this.value_4;
        //                goto case 4;
        //            case 4:
        //                this.value_4 = this.value_5;
        //                goto case 5;
        //            case 5:
        //                this.value_5 = default;
        //                break;
        //        }
        //        if (this.count == 4)
        //        {
        //            return new ThreeItemCollection(this.value_0, this.value_1, this.value_2);
        //        }
        //        else
        //        {
        //            this.count--;
        //            return this;
        //        }
        //    }
        //    internal override object SetAt(int index, T item)
        //    {
        //        if (index < 0 || index >= this.count)
        //        {
        //            throw new ArgumentOutOfRangeException();
        //        }
        //        switch (index)
        //        {
        //            case 0:
        //                this.value_0 = item;
        //                break;
        //            case 1:
        //                this.value_1 = item;
        //                break;
        //            case 2:
        //                this.value_2 = item;
        //                break;
        //            case 3:
        //                this.value_3 = item;
        //                break;
        //            case 4:
        //                this.value_4 = item;
        //                break;
        //            case 5:
        //                this.value_5 = item;
        //                break;
        //        }
        //        return this;
        //    }
        //    internal override bool TryGet(T item, out T existingItem)
        //    {
        //        if (Comparer.Equals(item, this.value_0))
        //        {
        //            existingItem = this.value_0;
        //            return true;
        //        }
        //        if (Comparer.Equals(item, this.value_1))
        //        {
        //            existingItem = this.value_1;
        //            return true;
        //        }
        //        if (Comparer.Equals(item, this.value_2))
        //        {
        //            existingItem = this.value_2;
        //            return true;
        //        }
        //        if (Comparer.Equals(item, this.value_3))
        //        {
        //            existingItem = this.value_3;
        //            return true;
        //        }
        //        if (this.count > 4 && Comparer.Equals(item, this.value_4))
        //        {
        //            existingItem = this.value_4;
        //            return true;
        //        }
        //        if (this.count > 5 && Comparer.Equals(item, this.value_5))
        //        {
        //            existingItem = this.value_5;
        //            return true;
        //        }
        //        existingItem = default;
        //        return false;
        //    }
        //    private ICollection<T> CreateFullCollection(CollectionCreator<ICollection<T>> fullListCreator)
        //    {
        //        var list = fullListCreator();
        //        list.Add(this.value_0);
        //        list.Add(this.value_1);
        //        list.Add(this.value_2);
        //        list.Add(this.value_3);
        //        list.Add(this.value_4);
        //        list.Add(this.value_5);
        //        return list;
        //    }
        //}
        private sealed class ThreeItemCollection : SmallCollection<T>
        {
            private T value_0;

            private T value_1;

            private T value_2;

            internal ThreeItemCollection()
            {
            }

            internal ThreeItemCollection(T value_0, T value_1, T value_2)
            {
                this.value_0 = value_0;
                this.value_1 = value_1;
                this.value_2 = value_2;
            }

            public override int Count
            {
                get { return 3; }
            }

            public override T this[int index]
            {
                get
                {
                    switch (index)
                    {
                        case 0:
                            return this.value_0;
                        case 1:
                            return this.value_1;
                        case 2:
                            return this.value_2;
                    }

                    throw new ArgumentOutOfRangeException(nameof(index));
                }
            }

            public override SmallCollection<T> Clone()
            {
                return new ThreeItemCollection(this.value_0, this.value_1, this.value_2);
            }

            public override void CopyTo(T[] array, int arrayIndex)
            {
                array[arrayIndex] = this.value_0;
                array[arrayIndex + 1] = this.value_1;
                array[arrayIndex + 2] = this.value_2;
            }

            public override IEnumerator<T> GetEnumerator()
            {
                yield return this.value_0;
                yield return this.value_1;
                yield return this.value_2;
            }

            public override int IndexOf(T item)
            {
                if (Comparer.Equals(this.value_0, item))
                {
                    return 0;
                }

                if (Comparer.Equals(this.value_1, item))
                {
                    return 1;
                }

                if (Comparer.Equals(this.value_2, item))
                {
                    return 2;
                }

                return -1;
            }

            internal override object Add(T item, CollectionCreator<ICollection<T>> fullListCreator)
            {
                return new FourItemCollection(this.value_0, this.value_1, this.value_2, item);
            }

            internal override object Insert(int index, T item, CollectionCreator<IList<T>> fullSetCreator)
            {
                switch (index)
                {
                    case 0:
                        return new FourItemCollection(item, this.value_0, this.value_1, this.value_2);
                    case 1:
                        return new FourItemCollection(this.value_0, item, this.value_1, this.value_2);
                    case 2:
                        return new FourItemCollection(this.value_0, this.value_1, item, this.value_2);
                    case 3:
                        return new FourItemCollection(this.value_0, this.value_1, this.value_2, item);
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }

            internal override object RemoveAt(int index)
            {
                switch (index)
                {
                    case 0:
                        return new TwoItemCollection(this.value_1, this.value_2);
                    case 1:
                        return new TwoItemCollection(this.value_0, this.value_2);
                    case 2:
                        return new TwoItemCollection(this.value_0, this.value_1);
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }

            internal override object SetAt(int index, T item)
            {
                switch (index)
                {
                    case 0:
                        this.value_0 = item;
                        break;
                    case 1:
                        this.value_1 = item;
                        break;
                    case 2:
                        this.value_2 = item;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(index));
                }

                return this;
            }

            internal override bool TryGet(T item, out T existingItem)
            {
                if (Comparer.Equals(this.value_0, item))
                {
                    existingItem = this.value_0;
                    return true;
                }

                if (Comparer.Equals(this.value_1, item))
                {
                    existingItem = this.value_1;
                    return true;
                }

                if (Comparer.Equals(this.value_2, item))
                {
                    existingItem = this.value_2;
                    return true;
                }

                existingItem = default;
                return false;
            }
        }

        private sealed class TwoItemCollection : SmallCollection<T>
        {
            private T value_0;

            private T value_1;

            internal TwoItemCollection()
            {
            }

            internal TwoItemCollection(T value_0, T value_1)
            {
                this.value_0 = value_0;
                this.value_1 = value_1;
            }

            public override int Count
            {
                get { return 2; }
            }

            public override T this[int index]
            {
                get
                {
                    switch (index)
                    {
                        case 0:
                            return this.value_0;
                        case 1:
                            return this.value_1;
                    }

                    throw new ArgumentOutOfRangeException(nameof(index));
                }
            }

            public override SmallCollection<T> Clone()
            {
                return new TwoItemCollection(this.value_0, this.value_1);
            }

            public override bool Contains(T item)
            {
                return
                       Comparer.Equals(this.value_0, item)
                    || Comparer.Equals(this.value_1, item);
            }

            public override void CopyTo(T[] array, int arrayIndex)
            {
                array[arrayIndex] = this.value_0;
                array[arrayIndex + 1] = this.value_1;
            }

            public override IEnumerator<T> GetEnumerator()
            {
                yield return this.value_0;
                yield return this.value_1;
            }

            public override int IndexOf(T item)
            {
                if (Comparer.Equals(this.value_0, item))
                {
                    return 0;
                }

                if (Comparer.Equals(this.value_1, item))
                {
                    return 1;
                }

                return -1;
            }

            internal override object Add(T item, CollectionCreator<ICollection<T>> fullListCreator)
            {
                return new ThreeItemCollection(this.value_0, this.value_1, item);
            }

            internal override bool AddToSet(T item, CollectionCreator<ICollection<T>> fullSetCreator, out object newSet)
            {
                if (!this.Contains(item))
                {
                    newSet = new ThreeItemCollection(this.value_0, this.value_1, item);
                    return true;
                }

                newSet = this;
                return false;
            }

            internal override object Insert(int index, T item, CollectionCreator<IList<T>> fullSetCreator)
            {
                switch (index)
                {
                    case 0:
                        return new ThreeItemCollection(item, this.value_0, this.value_1);
                    case 1:
                        return new ThreeItemCollection(this.value_0, item, this.value_1);
                    case 2:
                        return new ThreeItemCollection(this.value_0, this.value_1, item);

                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }

            internal override bool Remove(T item, out object newSet)
            {
                if (Comparer.Equals(this.value_0, item))
                {
                    newSet = this.value_1 != null ? (object)this.value_1 : NullItem;
                    return true;
                }

                if (Comparer.Equals(this.value_1, item))
                {
                    newSet = this.value_0 != null ? (object)this.value_0 : NullItem;
                    return true;
                }

                newSet = this;
                return false;
            }

            internal override object RemoveAt(int index)
            {
                if (index == 0)
                {
                    return this.value_1 != null ? (object)this.value_1 : NullItem;
                }
                if (index == 1)
                {
                    return this.value_0 != null ? (object)this.value_0 : NullItem;
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }

            internal override object SetAt(int index, T item)
            {
                switch (index)
                {
                    case 0:
                        this.value_0 = item;
                        break;
                    case 1:
                        this.value_1 = item;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(index));
                }

                return this;
            }

            internal override bool TryGet(T item, out T existingItem)
            {
                if (Comparer.Equals(this.value_0, item))
                {
                    existingItem = this.value_0;
                    return true;
                }

                if (Comparer.Equals(this.value_1, item))
                {
                    existingItem = this.value_1;
                    return true;
                }

                existingItem = default;
                return false;
            }
        }
    }
}
