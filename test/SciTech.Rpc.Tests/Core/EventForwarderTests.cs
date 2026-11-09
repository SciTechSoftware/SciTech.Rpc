using NUnit.Framework;
using SciTech.ComponentModel;
using System;
using System.Collections.Generic;

namespace SciTech.Rpc.Tests.Core
{
    [TestFixture]
    public class EventForwarderTests
    {
        [Test]
        public void Add_Generic_ForwardsWithForwarderSender()
        {
            var source = new EventSource();
            var wrapper = new EventWrapper(source);
            var received = new List<(object? Sender, int Value)>();
            wrapper.ValueChanged += (s, e) => received.Add((s, e.Value));

            source.RaiseValueChanged(1);

            Assert.That(received, Is.EqualTo(new[] { ((object?)wrapper, 1) }));
        }

        [Test]
        public void Add_NonGeneric_ForwardsWithForwarderSender()
        {
            var source = new EventSource();
            var wrapper = new EventWrapper(source);
            object? receivedSender = null;
            wrapper.Changed += (s, e) => receivedSender = s;

            source.RaiseChanged();

            Assert.That(receivedSender, Is.SameAs(wrapper));
        }

        [Test]
        public void Add_Null_ReturnsNull()
        {
            var forwarder = new EventForwarder(this);

            Assert.That(forwarder.Add((EventHandler?)null), Is.Null);
            Assert.That(forwarder.Add((EventHandler<ValueEventArgs>?)null), Is.Null);
            Assert.That(forwarder.Add<ValueEventArgs, TextEventArgs>(null, ToText), Is.Null);
        }

        [Test]
        public void Add_SameHandlerTwice_IsForwardedTwiceUntilRemovedTwice()
        {
            var source = new EventSource();
            var wrapper = new EventWrapper(source);
            int count = 0;
            EventHandler<ValueEventArgs> handler = (s, e) => count++;

            wrapper.ValueChanged += handler;
            wrapper.ValueChanged += handler;
            source.RaiseValueChanged(1);
            wrapper.ValueChanged -= handler;
            source.RaiseValueChanged(2);
            wrapper.ValueChanged -= handler;
            source.RaiseValueChanged(3);

            Assert.That(count, Is.EqualTo(3));
            Assert.That(source.HasValueChangedHandlers, Is.False);
        }

        [Test]
        public void Add_WithConverter_ForwardsConvertedArgsAndSkipsNull()
        {
            var source = new EventSource();
            var wrapper = new EventWrapper(source);
            var received = new List<string>();
            wrapper.TextChanged += (s, e) => received.Add(e.Text);

            source.RaiseValueChanged(1);
            source.RaiseValueChanged(-1);
            source.RaiseValueChanged(2);

            Assert.That(received, Is.EqualTo(new[] { "1", "2" }));
        }

        [Test]
        public void Remove_AfterAdd_StopsForwarding()
        {
            var source = new EventSource();
            var wrapper = new EventWrapper(source);
            int changedCount = 0;
            int valueCount = 0;
            int textCount = 0;
            EventHandler changedHandler = (s, e) => changedCount++;
            EventHandler<ValueEventArgs> valueHandler = (s, e) => valueCount++;
            EventHandler<TextEventArgs> textHandler = (s, e) => textCount++;
            wrapper.Changed += changedHandler;
            wrapper.ValueChanged += valueHandler;
            wrapper.TextChanged += textHandler;

            wrapper.Changed -= changedHandler;
            wrapper.ValueChanged -= valueHandler;
            wrapper.TextChanged -= textHandler;
            source.RaiseChanged();
            source.RaiseValueChanged(1);

            Assert.That((changedCount, valueCount, textCount), Is.EqualTo((0, 0, 0)));
            Assert.That(source.HasChangedHandlers, Is.False);
            Assert.That(source.HasValueChangedHandlers, Is.False);
        }

        [Test]
        public void Remove_NotAdded_ReturnsNull()
        {
            var forwarder = new EventForwarder(this);
            EventHandler<ValueEventArgs> handler = (s, e) => { };
            forwarder.Add<ValueEventArgs, TextEventArgs>((s, e) => { }, ToText);

            Assert.That(forwarder.Remove((EventHandler?)((s, e) => { })), Is.Null);
            Assert.That(forwarder.Remove(handler), Is.Null);
            Assert.That(forwarder.Remove<ValueEventArgs, TextEventArgs>((s, e) => { }, ToText), Is.Null);
        }

        [Test]
        public void Remove_WithOtherConverter_DoesNotRemove()
        {
            var forwarder = new EventForwarder(this);
            EventHandler<TextEventArgs> handler = (s, e) => { };
            forwarder.Add<ValueEventArgs, TextEventArgs>(handler, ToText);

            Assert.That(forwarder.Remove<ValueEventArgs, TextEventArgs>(handler, e => null), Is.Null);
            Assert.That(forwarder.Remove<ValueEventArgs, TextEventArgs>(handler, ToText), Is.Not.Null);
        }

        private static TextEventArgs? ToText(ValueEventArgs e)
            => e.Value >= 0 ? new TextEventArgs(e.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)) : null;

        private sealed class EventSource
        {
            public event EventHandler? Changed;

            public event EventHandler<ValueEventArgs>? ValueChanged;

            public bool HasChangedHandlers => this.Changed != null;

            public bool HasValueChangedHandlers => this.ValueChanged != null;

            public void RaiseChanged() => this.Changed?.Invoke(this, EventArgs.Empty);

            public void RaiseValueChanged(int value) => this.ValueChanged?.Invoke(this, new ValueEventArgs(value));
        }

        private sealed class EventWrapper
        {
            private static readonly Func<ValueEventArgs, TextEventArgs?> Converter = ToText;

            private readonly EventForwarder eventForwarder;

            private readonly EventSource source;

            public EventWrapper(EventSource source)
            {
                this.source = source;
                this.eventForwarder = new EventForwarder(this);
            }

            public event EventHandler? Changed
            {
                add => this.source.Changed += this.eventForwarder.Add(value);
                remove => this.source.Changed -= this.eventForwarder.Remove(value);
            }

            public event EventHandler<TextEventArgs>? TextChanged
            {
                add => this.source.ValueChanged += this.eventForwarder.Add(value, Converter);
                remove => this.source.ValueChanged -= this.eventForwarder.Remove(value, Converter);
            }

            public event EventHandler<ValueEventArgs>? ValueChanged
            {
                add => this.source.ValueChanged += this.eventForwarder.Add(value);
                remove => this.source.ValueChanged -= this.eventForwarder.Remove(value);
            }
        }

        private sealed class TextEventArgs : EventArgs
        {
            public TextEventArgs(string text)
            {
                this.Text = text;
            }

            public string Text { get; }
        }

        private sealed class ValueEventArgs : EventArgs
        {
            public ValueEventArgs(int value)
            {
                this.Value = value;
            }

            public int Value { get; }
        }
    }
}
