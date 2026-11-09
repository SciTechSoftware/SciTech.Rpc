#region Copyright notice and license
// Copyright (c) SciTech Software AB and contributors.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License.
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SciTech.ComponentModel
{
    /// <summary>
    /// Forwards the subscriptions to an event of another object, so that the handlers are called with the sender of
    /// this forwarder, and optionally with converted event args. The source event only has handlers while there are
    /// subscribers, e.g. RPC clients of a service implementation that exposes an event of the object it wraps.
    /// </summary>
    /// <example>
    /// <code>
    /// public event EventHandler&lt;ValueChangedEventArgs&gt;? ValueChanged
    /// {
    ///     add => this.owner.ValueChanged += this.eventForwarder.Add(value);
    ///     remove => this.owner.ValueChanged -= this.eventForwarder.Remove(value);
    /// }
    /// </code>
    /// </example>
    /// <remarks>
    /// A handler that is added several times is forwarded by the same delegate, and must be removed as many times.
    /// </remarks>
    public sealed class EventForwarder
    {
        private readonly Dictionary<DelegateKey, ForwardedEvent> forwardedEvents = new Dictionary<DelegateKey, ForwardedEvent>();

        private readonly object sender;

        private readonly object syncRoot = new object();

        /// <param name="sender">The sender of the forwarded events.</param>
        public EventForwarder(object sender)
        {
            this.sender = sender ?? throw new ArgumentNullException(nameof(sender));
        }

        /// <summary>
        /// Gets the handler to add to the source event, which calls <paramref name="value"/> with the sender of this
        /// forwarder.
        /// </summary>
        /// <returns>The forwarding handler, or <c>null</c> if <paramref name="value"/> is <c>null</c>.</returns>
        [return: NotNullIfNotNull(nameof(value))]
        public EventHandler<TEventArgs>? Add<TEventArgs>(EventHandler<TEventArgs>? value)
        {
            if (value == null) return null;

            var sender = this.sender;
            return (EventHandler<TEventArgs>)this.AddForwarder(
                new DelegateKey(value, null),
                () => new EventHandler<TEventArgs>((s, e) => value(sender, e)));
        }

        /// <summary>
        /// Gets the handler to add to the source event, which calls <paramref name="value"/> with the sender of this
        /// forwarder and the event args converted by <paramref name="converter"/>.
        /// </summary>
        /// <param name="converter">Converts the event args of the source event. If it returns <c>null</c>, the event
        /// is not forwarded. An equal delegate, i.e. with the same method and target, must be passed to
        /// <see cref="Remove{TSourceEventArgs, TEventArgs}(EventHandler{TEventArgs}, Func{TSourceEventArgs, TEventArgs})"/>,
        /// e.g. a static method or a lambda that captures nothing. A lambda that captures variables gets a new target
        /// each time it's evaluated.</param>
        /// <returns>The forwarding handler, or <c>null</c> if <paramref name="value"/> is <c>null</c>.</returns>
        [return: NotNullIfNotNull(nameof(value))]
        public EventHandler<TSourceEventArgs>? Add<TSourceEventArgs, TEventArgs>(EventHandler<TEventArgs>? value, Func<TSourceEventArgs, TEventArgs?> converter)
            where TEventArgs : class
        {
            if (value == null) return null;
            if (converter == null) throw new ArgumentNullException(nameof(converter));

            var sender = this.sender;
            return (EventHandler<TSourceEventArgs>)this.AddForwarder(
                new DelegateKey(value, converter),
                () => new EventHandler<TSourceEventArgs>((s, e) =>
                {
                    var forwardedArgs = converter(e);
                    if (forwardedArgs != null) value(sender, forwardedArgs);
                }));
        }

        /// <inheritdoc cref="Add{TEventArgs}(EventHandler{TEventArgs})"/>
        [return: NotNullIfNotNull(nameof(value))]
        public EventHandler? Add(EventHandler? value)
        {
            if (value == null) return null;

            var sender = this.sender;
            return (EventHandler)this.AddForwarder(
                new DelegateKey(value, null),
                () => new EventHandler((s, e) => value(sender, e)));
        }

        /// <summary>
        /// Gets the handler to remove from the source event, i.e. the handler returned when <paramref name="value"/>
        /// was added.
        /// </summary>
        /// <returns>The forwarding handler, or <c>null</c> if <paramref name="value"/> has not been added.</returns>
        public EventHandler<TEventArgs>? Remove<TEventArgs>(EventHandler<TEventArgs>? value)
            => value != null ? (EventHandler<TEventArgs>?)this.RemoveForwarder(new DelegateKey(value, null)) : null;

        /// <inheritdoc cref="Remove{TEventArgs}(EventHandler{TEventArgs})"/>
        /// <param name="converter">The converter that <paramref name="value"/> was added with.</param>
        public EventHandler<TSourceEventArgs>? Remove<TSourceEventArgs, TEventArgs>(EventHandler<TEventArgs>? value, Func<TSourceEventArgs, TEventArgs?> converter)
            where TEventArgs : class
            => value != null ? (EventHandler<TSourceEventArgs>?)this.RemoveForwarder(new DelegateKey(value, converter)) : null;

        /// <inheritdoc cref="Remove{TEventArgs}(EventHandler{TEventArgs})"/>
        public EventHandler? Remove(EventHandler? value)
            => value != null ? (EventHandler?)this.RemoveForwarder(new DelegateKey(value, null)) : null;

        private Delegate AddForwarder(DelegateKey key, Func<Delegate> createForwarder)
        {
            lock (this.syncRoot)
            {
                if (this.forwardedEvents.TryGetValue(key, out var forwardedEvent))
                {
                    this.forwardedEvents[key] = new ForwardedEvent(forwardedEvent.Forwarder, forwardedEvent.Count + 1);
                    return forwardedEvent.Forwarder;
                }

                var forwarder = createForwarder();
                this.forwardedEvents.Add(key, new ForwardedEvent(forwarder, 1));
                return forwarder;
            }
        }

        private Delegate? RemoveForwarder(DelegateKey key)
        {
            lock (this.syncRoot)
            {
                if (!this.forwardedEvents.TryGetValue(key, out var forwardedEvent))
                {
                    return null;
                }

                if (forwardedEvent.Count == 1)
                {
                    this.forwardedEvents.Remove(key);
                }
                else
                {
                    this.forwardedEvents[key] = new ForwardedEvent(forwardedEvent.Forwarder, forwardedEvent.Count - 1);
                }

                return forwardedEvent.Forwarder;
            }
        }

        private readonly struct DelegateKey : IEquatable<DelegateKey>
        {
            private readonly Delegate? converter;

            private readonly Delegate eventHandler;

            internal DelegateKey(Delegate eventHandler, Delegate? converter)
            {
                this.eventHandler = eventHandler;
                this.converter = converter;
            }

            public bool Equals(DelegateKey other)
                => this.eventHandler.Equals(other.eventHandler) && Equals(this.converter, other.converter);

            public override bool Equals(object? obj)
                => obj is DelegateKey other && this.Equals(other);

            public override int GetHashCode()
                => unchecked((this.eventHandler.GetHashCode() * 397) ^ (this.converter?.GetHashCode() ?? 0));
        }

        private readonly struct ForwardedEvent
        {
            internal ForwardedEvent(Delegate forwarder, int count)
            {
                this.Forwarder = forwarder;
                this.Count = count;
            }

            internal int Count { get; }

            internal Delegate Forwarder { get; }
        }
    }
}
