using SciTech.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.ComponentModel
{
    /// <summary>
    /// Represents a component that is owned by a dependent component.
    /// <para>This is a covariant interface that allows an <see cref="IOwned{T}"/> instance to be cast to
    /// an <c>IOwned</c> instance for a base type of <typeparamref name="T"/>.</para>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IOwned<out T> : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Gets the owned value.
        /// </summary>
        T Value { get; }
        
        bool CanDispose { get; }

        IOwned<T> ToUnowned();
    }

    public static class OwnedObject
    {
        public static IOwned<T> Create<T>(T value)
        {
            return new OwnedObject<T>(value);
        }

        public static IOwned<T> CreateUnowned<T>(T value)
        {
            return new OwnedObject<T>(value, (Action?)null);
        }

        public static IOwned<T> Create<T>(T value, Action disposeAction)
        {
            return new OwnedObject<T>(value, disposeAction);
        }

        public static IOwned<T> Create<T>(T value, Func<ValueTask> disposeAction)
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

        public OwnedObject(T value, IAsyncDisposable? objectLifetime)
        {
            this.Value = value;
            this.disposeActionOrDisposable = objectLifetime;
        }

        public OwnedObject(T value, Func<ValueTask>? disposeAction)
        {
            this.Value = value;
            this.disposeActionOrDisposable = disposeAction;
        }

        public OwnedObject(T value)
        {
            this.Value = value;
            if (value is IAsyncDisposable || value is IDisposable)
            {
                this.disposeActionOrDisposable = value;
            }
        }

        public T Value { get; }

        public bool CanDispose => this.disposeActionOrDisposable != null;

        public IOwned<T> ToUnowned()
        {
            if( this.disposeActionOrDisposable != null )
            {
                return new OwnedObject<T>(this.Value, (Action?)null);
            }

            return this;
        }

        public void Dispose()
        {
            var disposeActionOrDisposable = Interlocked.CompareExchange(ref this.disposeActionOrDisposable, null, this.disposeActionOrDisposable);

            // Checking delegates first, under the assumption that the check might be slightly faster (inheritance
            // lookup should be faster for a sealed class compared to an interface).
            if (disposeActionOrDisposable is Action action)
            {
                action.Invoke();
            }
            else if (disposeActionOrDisposable is Func<ValueTask> asyncAction)
            {
                // This is a bad code path. If an OwnedObject is created with a ValueTask functon
                // it should be disposed asynchronously. Not much to do about it here though.
                asyncAction.Invoke().AsTask().AwaiterResult();
            }
            else if (disposeActionOrDisposable is IDisposable disposable)
            {
                disposable.Dispose();
            }
            else if(disposeActionOrDisposable is IAsyncDisposable asyncDisposable)
            {
                // This is a bad code path. If an OwnedObject is created only with an IAsyncDisposable
                // it should be disposed asynchronously. Not much to do about it here though.
                asyncDisposable.DisposeAsync().AsTask().AwaiterResult();
            } 
        }

        public ValueTask DisposeAsync()
        {
            var disposeActionOrDisposable = Interlocked.CompareExchange(ref this.disposeActionOrDisposable, null, this.disposeActionOrDisposable);

            // Checking delegates first, under the assumption that the check might be slightly faster (inheritance
            // lookup should be faster for a sealed class compared to an interface).
            if (disposeActionOrDisposable is Func<ValueTask> asyncAction)
            {
                return asyncAction.Invoke();
            }
            else if (disposeActionOrDisposable is Action action)
            {
                action.Invoke();
            }
            else if (disposeActionOrDisposable is IAsyncDisposable asyncDisposable)
            {
                return asyncDisposable.DisposeAsync();
            }           
            else if (disposeActionOrDisposable is IDisposable disposable)
            {
                disposable.Dispose();
            }

            return default;
        }
    }
}