using SciTech.Diagnostics;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Threading
{
#pragma warning disable CA1062 // Validate arguments of public methods
    public static class TaskExtensions
    {
        private static volatile Action<Exception>? defaultExceptionHandler;

        public static Action<Exception>? DefaultExceptionHandler
        {
            get
            {
                return defaultExceptionHandler;
            }
            set
            {
                defaultExceptionHandler = value;
            }
        }

        public static Task AsTask(this CancellationToken token)
        {
            var tcs = new TaskCompletionSource<bool>();

            token.Register(() => tcs.SetResult(true));

            return tcs.Task;
        }

        public static void AwaiterResult(this Task task)
        {
            task.GetAwaiter().GetResult();
        }

        public static T AwaiterResult<T>(this Task<T> task)
        {
            return task.GetAwaiter().GetResult();
        }
        public static void AwaiterResult(this ValueTask task)
        {
            task.GetAwaiter().GetResult();
        }

        public static T AwaiterResult<T>(this ValueTask<T> task)
        {
            return task.GetAwaiter().GetResult();
        }

        public static ConfiguredTaskAwaitable<T> ContextFree<T>(this Task<T> task)
        {
            return task.ConfigureAwait(false);
        }

        public static ConfiguredTaskAwaitable ContextFree(this Task task)
        {
            return task.ConfigureAwait(false);
        }

        public static ConfiguredValueTaskAwaitable<T> ContextFree<T>(this ValueTask<T> task)
        {
            return task.ConfigureAwait(false);
        }

        public static ConfiguredValueTaskAwaitable ContextFree(this ValueTask task)
        {
            return task.ConfigureAwait(false);
        }

        public static void ExpectCompleted(this Task task)
        {
            var status = task.Status;
            switch (status)
            {
                case TaskStatus.Canceled:
                //throw new OperationCanceledException();
                case TaskStatus.Faulted:

                //if (task.Exception.InnerExceptions.Count > 0)
                //{
                //    throw task.Exception.InnerExceptions[0];
                //}

                //throw task.Exception;
                case TaskStatus.RanToCompletion:
                    break;
                default:
                    throw new InvalidOperationException("Synchronous task is not completed.");
            }
        }

        public static void Forget(this Task task)
        {
            Forget(task, DefaultExceptionHandler);
        }

#pragma warning disable IDE0060 // Remove unused parameter
        public static void Forget(this ValueTask task)
        {
            // TODO: Cannot use ContinueWith without creating a full Task.
            // So exceptions will not be propagated to the DefaultExceptionHandler.
        }

#pragma warning restore IDE0060 // Remove unused parameter

        public static void Forget(this Task task, Action<Exception>? exceptionHandler)
        {
            if (exceptionHandler != null)
            {
                task.ContinueWith(t => exceptionHandler(t.Exception!), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            }
        }

        public static void Forget(this Task task, IExceptionHandler exceptionHandler)
        {
            if (exceptionHandler != null)
            {
                task.ContinueWith(t => exceptionHandler.HandleException(t.Exception!, UnexpectedExceptionAction.Handle), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            }
        }

        public static TResult GetSyncResult<TResult>(this Task<TResult> task)
        {
            task.ExpectCompleted();

            return task.GetAwaiter().GetResult();
        }

        public static ConfiguredTaskAwaitable<T> WithContext<T>(this Task<T> task)
        {
            return task.ConfigureAwait(true);
        }

        public static ConfiguredTaskAwaitable WithContext(this Task task)
        {
            return task.ConfigureAwait(true);
        }

        public static ConfiguredValueTaskAwaitable<T> WithContext<T>(this ValueTask<T> task)
        {
            return task.ConfigureAwait(true);
        }

        public static ConfiguredValueTaskAwaitable WithContext(this ValueTask task)
        {
            return task.ConfigureAwait(true);
        }
    }
#pragma warning restore CA1062 // Validate arguments of public methods
}
