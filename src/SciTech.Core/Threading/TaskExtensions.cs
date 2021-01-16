#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB and TA Instrument Inc.
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE

// Parts of code taken from the AsyncEx library (https://github.com/StephenCleary/AsyncEx)
// 
// The MIT License(MIT)
//
// Copyright(c) 2014 StephenCleary
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.

// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
#endregion

using SciTech.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Threading
{
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

        public static void AwaiterResult(this Task task)
        {
            if (task is null) throw new ArgumentNullException(nameof(task));

            task.GetAwaiter().GetResult();
        }

        public static T AwaiterResult<T>(this Task<T> task)
        {
            if (task is null) throw new ArgumentNullException(nameof(task));

            return task.GetAwaiter().GetResult();
        }

        public static ConfiguredTaskAwaitable<T> ContextFree<T>(this Task<T> task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));

            return task.ConfigureAwait(false);
        }

        public static ConfiguredTaskAwaitable ContextFree(this Task task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));

            return task.ConfigureAwait(false);
        }

        public static ConfiguredAsyncDisposable ContextFree(this IAsyncDisposable asyncDisposable)
        {
            if (asyncDisposable == null) throw new ArgumentNullException(nameof(asyncDisposable));

            return asyncDisposable.ConfigureAwait(false);
        }

        public static ConfiguredAsyncDisposable WithContext(this IAsyncDisposable asyncDisposable)
        {
            if (asyncDisposable == null) throw new ArgumentNullException(nameof(asyncDisposable));

            return asyncDisposable.ConfigureAwait(true);
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
            if (task == null) throw new ArgumentNullException(nameof(task));

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

        public static void Forget(this ValueTask task)
        {
            task.Preserve();
        }

        public static void Forget(this Task task, Action<Exception>? exceptionHandler)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));

            if (!task.IsCompleted || task.IsFaulted)
            {
                _ = task.ContinueWith(t => exceptionHandler?.Invoke(t.Exception!), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            }        
        }

        public static void Forget(this Task task, IExceptionHandler exceptionHandler)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));

            if (!task.IsCompleted || task.IsFaulted)
            {
                _ = task.ContinueWith(t => exceptionHandler?.HandleException(t.Exception!, UnexpectedExceptionAction.Handle), 
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            }
        }

        public static TResult GetSyncResult<TResult>(this Task<TResult> task)
        {
            task.ExpectCompleted();

            return task.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Asynchronously waits for the task to complete, or for the cancellation token to be canceled.
        /// </summary>
        /// <param name="task">The task to wait for. May not be <c>null</c>.</param>
        /// <param name="cancellationToken">The cancellation token that cancels the wait.</param>
        public static Task WaitAsync(this Task task, CancellationToken cancellationToken)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            if (!cancellationToken.CanBeCanceled)
                return task;
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);
            return DoWaitAsync(task, cancellationToken);
        }

        /// <summary>
        /// Asynchronously waits for the task to complete, or for the cancellation token to be canceled.
        /// </summary>
        /// <typeparam name="TResult">The type of the task result.</typeparam>
        /// <param name="task">The task to wait for. May not be <c>null</c>.</param>
        /// <param name="cancellationToken">The cancellation token that cancels the wait.</param>
        public static Task<TResult> WaitAsync<TResult>(this Task<TResult> task, CancellationToken cancellationToken)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            if (!cancellationToken.CanBeCanceled)
                return task;
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<TResult>(cancellationToken);
            return DoWaitAsync(task, cancellationToken);
        }

        /// <summary>
        /// Asynchronously waits for all of the source tasks to complete.
        /// </summary>
        /// <param name="task">The tasks to wait for. May not be <c>null</c>.</param>
        public static Task WhenAll(this IEnumerable<Task> task)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            return Task.WhenAll(task);
        }

        /// <summary>
        /// Asynchronously waits for all of the source tasks to complete.
        /// </summary>
        /// <typeparam name="TResult">The type of the task results.</typeparam>
        /// <param name="task">The tasks to wait for. May not be <c>null</c>.</param>
        public static Task<TResult[]> WhenAll<TResult>(this IEnumerable<Task<TResult>> task)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            return Task.WhenAll(task);
        }

        /// <summary>
        /// Asynchronously waits for any of the source tasks to complete.
        /// </summary>
        /// <param name="task">The tasks to wait for. May not be <c>null</c>.</param>
        public static Task<Task> WhenAny(this IEnumerable<Task> task)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            return Task.WhenAny(task);
        }

        /// <summary>
        /// Asynchronously waits for any of the source tasks to complete.
        /// </summary>
        /// <typeparam name="TResult">The type of the task results.</typeparam>
        /// <param name="task">The tasks to wait for. May not be <c>null</c>.</param>
        public static Task<Task<TResult>> WhenAny<TResult>(this IEnumerable<Task<TResult>> task)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            return Task.WhenAny(task);
        }

        /// <summary>
        /// Asynchronously waits for any of the source tasks to complete, or for the cancellation token to be canceled.
        /// </summary>
        /// <param name="task">The tasks to wait for. May not be <c>null</c>.</param>
        /// <param name="cancellationToken">The cancellation token that cancels the wait.</param>
        public static Task<Task> WhenAny(this IEnumerable<Task> task, CancellationToken cancellationToken)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            return Task.WhenAny(task).WaitAsync(cancellationToken);
        }

        /// <summary>
        /// Asynchronously waits for any of the source tasks to complete, or for the cancellation token to be canceled.
        /// </summary>
        /// <typeparam name="TResult">The type of the task results.</typeparam>
        /// <param name="task">The tasks to wait for. May not be <c>null</c>.</param>
        /// <param name="cancellationToken">The cancellation token that cancels the wait.</param>
        public static Task<Task<TResult>> WhenAny<TResult>(this IEnumerable<Task<TResult>> task, CancellationToken cancellationToken)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            return Task.WhenAny(task).WaitAsync(cancellationToken);
        }

        public static ConfiguredTaskAwaitable<T> WithContext<T>(this Task<T> task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));

            return task.ConfigureAwait(true);
        }

        public static ConfiguredTaskAwaitable WithContext(this Task task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));

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

        private static async Task DoWaitAsync(Task task, CancellationToken cancellationToken)
        {
            using var cancelTaskSource = new CancellationTokenTaskSource<object>(cancellationToken);

            await (await Task.WhenAny(task, cancelTaskSource.Task).ContextFree()).ContextFree();
        }

        private static async Task<TResult> DoWaitAsync<TResult>(Task<TResult> task, CancellationToken cancellationToken)
        {
            using var cancelTaskSource = new CancellationTokenTaskSource<TResult>(cancellationToken);

            return await (await Task.WhenAny(task, cancelTaskSource.Task).ContextFree()).ContextFree();
        }
    }
}
