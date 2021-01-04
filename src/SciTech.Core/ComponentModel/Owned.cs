using System;
using System.Threading;

namespace SciTech.ComponentModel
{
    /// <summary>
    /// Represents a component that is owned by a dependent component.
    /// <para>This is a covariant interface that allows an <see cref="IOwned{T}"/> instance to be cast to
    /// an <c>IOwned</c> instance for a base type of <typeparamref name="T"/>.</para>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IOwned<out T> : IDisposable
    {
        /// <summary>
        /// Gets the owned value.
        /// </summary>
        T Value { get; }
    }

    public static class OwnedObject
    {
        public static IOwned<T> Create<T>(T value)
        {
            return new OwnedObject<T>(value);
        }

        public static IOwned<T> Create<T>(T value, Action disposeAction)
        {
            return new OwnedObject<T>(value, disposeAction);
        }
    }

    /// <summary>
    /// Default <see cref="IOwned{T}"/> implementation.
    /// </summary>
    /// <typeparam name="T">Type of the owned instance.</typeparam>
    public class OwnedObject<T> : IOwned<T>
    {
        private object? disposeActionOrDisposable;

        public OwnedObject(T value, IDisposable? objectLifetime)
        {
            this.Value = value;
            this.disposeActionOrDisposable = objectLifetime;
        }

        public OwnedObject(T value, Action? disposeAction)
        {
            this.Value = value;
            this.disposeActionOrDisposable = disposeAction;
        }

        public OwnedObject(T value)
        {
            this.Value = value;
            this.disposeActionOrDisposable = value as IDisposable;
        }

        public T Value { get; }

        public void Dispose()
        {
            var disposeActionOrDisposable = Interlocked.CompareExchange(ref this.disposeActionOrDisposable, null, this.disposeActionOrDisposable);
            if (disposeActionOrDisposable is IDisposable disposable)
            {
                disposable.Dispose();
            }
            else if (disposeActionOrDisposable is Action action)
            {
                action.Invoke();
            }
        }
    }
}