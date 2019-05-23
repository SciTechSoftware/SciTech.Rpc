using System;
using System.Collections;
using System.Collections.Generic;

namespace SciTech.Collections
{
    internal class SingleEnumerator<T> : IEnumerator<T>
    {
        private readonly T item;

        private int index;

        internal SingleEnumerator(T item)
        {
            this.item = item;
            this.index = -1;
        }

        public T Current
        {
            get
            {
                if (this.index != 0)
                    throw new InvalidOperationException();
                return this.item;
            }
        }

        object IEnumerator.Current
        {
            get { return this.Current; }
        }

        public void Dispose()
        {
        }

        public bool MoveNext()
        {
            if (this.index == -1)
            {
                this.index = 0;
                return true;
            }
            return false;
        }

        public void Reset()
        {
            this.index = -1;
        }
    }
}
