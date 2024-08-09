#nullable enable 
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Threading
{

    public static class SynchronizationContextExtensions
    {
        private static readonly SendOrPostCallback ActionCallback = s => ((Action)s!)();
        // private static readonly SendOrPostCallback AsyncCallback = s => ((Func<Task>)s!)().Forget();

        public static void TryPost(this SynchronizationContext? syncContext, Action? action)
        {
            if (action != null)
            {
                if (syncContext != null)
                {
                    syncContext.Post(ActionCallback, action);
                }
                else
                {
                    action();
                }
            }
        }

        public static void Post(this SynchronizationContext? syncContext, Action action)
        {
            ArgumentNullException.ThrowIfNull(syncContext);
            syncContext.Post(new SendOrPostCallback(_ => action()), null);
        }


        public static void Post(this SynchronizationContext? syncContext, Action<object?> action, object? state)
        {
            ArgumentNullException.ThrowIfNull(syncContext);
            syncContext.Post(new SendOrPostCallback(s => action(s)), state);
        }

        public static void RaiseEvent(this SynchronizationContext? syncContext, EventHandler? eventHandler, object source, EventArgs e)
        {
            if (eventHandler != null)
            {

                if (syncContext != null)
                {
                    syncContext.Post(_ => eventHandler(source, e), null);
                }
                else
                {
                    eventHandler(source, e);
                }
            }
        }

        public static void RaiseEvent(this SynchronizationContext? syncContext, Func<EventHandler?> eventHandlerRetriever, object source, EventArgs e)
        {
            if (syncContext != null)
            {
                syncContext.Post(_ => eventHandlerRetriever()?.Invoke(source, e), null);
            }
            else
            {
                eventHandlerRetriever?.Invoke()?.Invoke(source, e);
            }
        }

        public static void RaiseEventImmediate(this SynchronizationContext? syncContext, EventHandler? eventHandler, object source, EventArgs e)
        {
            if (eventHandler != null)
            {
                if( syncContext is ISuspendableSynchronizationContext suspendableContext)
                {
                    suspendableContext.PostImmediate(_ => eventHandler(source, e), null);
                } else  if (syncContext != null)
                {
                    syncContext.Post(_ => eventHandler(source, e), null);
                }
                else
                {
                    eventHandler(source, e);
                }
            }
        }


        public static void RaiseEvent<TEventArgs>(this SynchronizationContext? syncContext, EventHandler<TEventArgs>? eventHandler, object source, TEventArgs e)
        {
            if (eventHandler != null)
            {
                if (syncContext != null)
                {
                    syncContext.Post(_ => eventHandler(source, e), null);
                }
                else
                {
                    eventHandler(source, e);
                }
            }
        }
    }
}