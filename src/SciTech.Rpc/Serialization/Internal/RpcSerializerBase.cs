#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using System;
using System.Buffers;
using System.Collections.Generic;

namespace SciTech.Rpc.Serialization.Internal
{
    public abstract class RpcSerializerBase : IRpcSerializer
    {
        private readonly Dictionary<Type, WeakReference> cachedSerializers = new Dictionary<Type, WeakReference>();

        private readonly object syncRoot = new object();

        public abstract IRpcSerializer<T> CreateTyped<T>();

        public abstract object? Deserialize(ReadOnlySequence<byte> input, Type type);

        public abstract void Serialize(BufferWriterStream bufferWriter, object? input, Type type);


        protected object CreateTyped(Type type, Func<Type, object> serializerFactory)
        {
            if (serializerFactory is null) throw new ArgumentNullException(nameof(serializerFactory));

            lock (this.syncRoot)
            {
                if (this.cachedSerializers.TryGetValue(type, out var wrSerializer) && wrSerializer.Target is object serializer)
                {
                    return serializer;
                }
            }

            var newSerializer = serializerFactory(type);

            lock (this.syncRoot)
            {
                this.cachedSerializers[type] = new WeakReference(newSerializer);
            }

            return newSerializer;
        }
    }
}
