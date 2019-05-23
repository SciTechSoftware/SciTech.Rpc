using System;
using System.Threading;

namespace SciTech.Rpc
{
    public sealed class ScopedObject<TObject> : IDisposable where TObject : class
    {
        private Action? disposeAction;

        public ScopedObject(TObject value, Action? disposeAction)
        {
            this.Value = value ?? throw new ArgumentNullException(nameof(value));
            this.disposeAction = disposeAction;
        }

        public ScopedObject(TObject value) :  this(value, null)
        {
        }

        public TObject Value { get; }

        public ScopedObject<TNewObject> Cast<TNewObject>() where TNewObject : class
        {
            var action = Interlocked.CompareExchange(ref this.disposeAction, null, this.disposeAction);
            return new ScopedObject<TNewObject>(this.Value as TNewObject ?? throw new InvalidCastException(), action);

        }

        public void Dispose()
        {
            var action = Interlocked.CompareExchange(ref this.disposeAction, null, this.disposeAction);
            action?.Invoke();
        }
    }
}
