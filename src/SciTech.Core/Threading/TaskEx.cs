using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Threading
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix")]
    public static class TaskEx
    {
        public static Task Run(Action action, TaskScheduler scheduler, CancellationToken cancellationToken)
            => Task.Factory.StartNew(
                action,
                cancellationToken,
                TaskCreationOptions.DenyChildAttach,
                scheduler);

        public static Task Run(Action action, TaskScheduler scheduler)
            => Task.Factory.StartNew(
                action,
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                scheduler);

        public static Task Run(Func<Task> action, TaskScheduler scheduler, CancellationToken cancellationToken)    
            => Task.Factory.StartNew(
                action,
                cancellationToken,
                TaskCreationOptions.DenyChildAttach,
                scheduler).Unwrap();

        public static Task Run(Func<Task> action, TaskScheduler scheduler)
            => Task.Factory.StartNew(
                action,
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                scheduler).Unwrap();
    }
}
