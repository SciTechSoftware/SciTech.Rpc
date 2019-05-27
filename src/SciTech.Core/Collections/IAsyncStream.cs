using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SciTech.Collections
{
    public interface IAsyncStream<T>
    {
        T Current { get; }

        ValueTask<bool> MoveNextAsync();
    }
}
