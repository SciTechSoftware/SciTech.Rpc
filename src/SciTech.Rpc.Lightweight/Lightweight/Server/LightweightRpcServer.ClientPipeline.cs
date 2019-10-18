#region Copyright notice and license
// Copyright (c) 2019, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License. 
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//
#endregion

using Microsoft.Extensions.DependencyInjection;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Lightweight.Server.Internal;
using SciTech.Rpc.Serialization;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server
{
    public partial class LightweightRpcServer
    {
#pragma warning disable CA1031 // Do not catch general exception types
        /// <summary>
        /// Handles the communication with a connected client.
        /// </summary>
        private class ClientPipeline : RpcPipeline
        {
            private readonly Dictionary<int, ActiveOperation> activeOperations = new Dictionary<int, ActiveOperation>();

            private readonly LightweightRpcServer server;

            private readonly object syncRoot = new object();

            private IRpcSerializer serializer;

            public ClientPipeline(IDuplexPipe pipe, LightweightRpcServer server, int? maxRequestSize, int? maxResponseSize, bool skipLargeFrames)
                : base(pipe, maxResponseSize, maxRequestSize, skipLargeFrames)
            {
                this.server = server;
                this.serializer = server.Serializer;
            }

            public Task RunAsync(CancellationToken cancellationToken)
                => this.StartReceiveLoopAsync(cancellationToken);

            protected override void OnClosed(Exception? ex)
            {
                base.OnClosed(ex);

                ActiveOperation[] activeOps;
                lock (this.syncRoot)
                {
                    activeOps = this.activeOperations.Values.ToArray();
                    this.activeOperations.Clear();
                }

                // Try to cancel the active operations (if
                // there's a cancellation token).
                foreach (var activeOp in activeOps)
                {
                    activeOp.Cancel();
                }
            }

            protected override sealed ValueTask OnReceiveAsync(in LightweightRpcFrame frame)
            {
                ValueTask handleRequestTask;
                switch (frame.FrameType)
                {
                    case RpcFrameType.UnaryRequest:
                    case RpcFrameType.StreamingRequest:
                        handleRequestTask = this.HandleRequestAsync(frame);
                        break;
                    //case RpcFrameType.StreamingRequest:
                    //handleRequestTask = this.HandleStreamingRequestAsync(frame);
                    //break;
                    case RpcFrameType.CancelRequest:
                        {
                            // It is possible that the PipeScheduler is set to Inline (which 
                            // is a dangereous option), and then this cancellation may dead-lock-
                            // As a safety, handle the cancellation on a worker thread.
                            int messageId = frame.MessageNumber;
                            handleRequestTask = new ValueTask(Task.Run(() => this.HandleCancelRequestAsync(messageId)));
                            break;
                        }
                    default:
                        throw new NotImplementedException();
                }

                return handleRequestTask;
            }

            protected override Task OnReceiveLargeFrameAsync(LightweightRpcFrame frame)
            {
                string msg = $"Size of received request frame exceeds size limit (frame size={frame.FrameLength}, max size={this.MaxReceiveFrameLength}).";

                return this.WriteErrorResponseAsync(frame.MessageNumber, "", msg, RpcFailure.SizeLimitExceeded);
            }

            protected override void OnReceiveLoopFaulted(ExceptionEventArgs e)
            {
                base.OnReceiveLoopFaulted(e);

                this.server.RemoveClient(this);
                this.Close(e.Exception);
            }

            private void AddActiveOperation(ActiveOperation activeOperation)
            {
                lock (this.syncRoot)
                {
                    this.activeOperations.Add(activeOperation.MessageId, activeOperation);
                }
            }

            private ActiveOperation CreateActiveOperation(int messageId, ActiveOperation? activeOperation, CancellationTokenSource? cancellationSource)
            {
                if (activeOperation == null)
                {
                    activeOperation = new ActiveOperation(messageId, cancellationSource);
                    this.AddActiveOperation(activeOperation);
                }

                return activeOperation;
            }

            /// <summary>
            /// Handles an operation that could not be completed synchronously. Called when an operation handler has returned an unfinished task,
            /// or an error occurred.
            /// </summary>
            /// <param name="frame">The operation request frame.</param>
            /// <param name="activeOperation"></param>
            /// <param name="messageTask"></param>
            /// <returns>A task that should be awaited before the next frame is handled. Normally 
            /// just the default ValueTask, unless active operations have been throttled.</returns>
            private ValueTask HandleAsyncOperation(in LightweightRpcFrame frame, ActiveOperation activeOperation, IServiceScope? scope, Task messageTask)
            {
                // TODO: Throttle the number of active operations.

                int messageId = frame.MessageNumber;
                string operationName = frame.RpcOperation;

                async void HandleCompletedAsyncOperation(Task t)
                {
                    try
                    {
                        if (t.IsCanceled)
                        {
                            if (activeOperation.IsCancellationRequested)
                            {
                                await this.WriteCancelResponseAsync(messageId, operationName).ContextFree();
                            }
                            else
                            {
                                // If the operation is cancelled, but cancellation has not been requested, then
                                // it's actually a timeout.
                                await this.WriteTimeoutResponseAsync(messageId, operationName).ContextFree();
                            }
                        }
                        else if (t.IsFaulted)
                        {
                            // Note. It will currently get here if the operation handler failed to 
                            // write the response. Most likely we will not succeed now either (and the 
                            // pipe will be closed). Maybe the handler should close the pipe instead 
                            // and not propagate the error?
                            await this.WriteErrorResponseAsync(messageId, operationName, t.Exception!.InnerException ?? t.Exception).ContextFree();
                        }
                    }
                    catch (Exception e)
                    {
                        // TODO: Log
                        this.Close(e);  // Will not throw.
                    }
                    finally
                    {
                        this.RemoveActiveOperation(messageId);
                        scope?.Dispose();
                    }
                }

                messageTask.ContinueWith(HandleCompletedAsyncOperation, TaskScheduler.Default);

                //if( needsToThrottle )
                //{
                //    return throttleTask;
                //}

                return default;
            }

            private ValueTask HandleCancelRequestAsync(int messageId)
            {
                ActiveOperation? activeOperation;
                lock (this.syncRoot)
                {
                    this.activeOperations.TryGetValue(messageId, out activeOperation);
                }

                activeOperation?.Cancel();

                return default;
            }

            private ValueTask HandleRequestAsync(in LightweightRpcFrame frame)
            {
                IServiceScope? scope = null;
                ActiveOperation? activeOperation = null;
                CancellationTokenSource? cancellationSource = null;
                try
                {
                    var methodStub = this.server.GetMethodDefinition(frame.RpcOperation);
                    if (methodStub != null)
                    {
                        bool canCancel = (frame.OperationFlags & RpcOperationFlags.CanCancel) != 0;

                        if (canCancel || frame.Timeout > 0)
                        {
                            cancellationSource = new CancellationTokenSource();
                            if (canCancel)
                            {
                                activeOperation = this.CreateActiveOperation(frame.MessageNumber, activeOperation, cancellationSource);
                            }

                            if (frame.Timeout > 0)
                            {
                                cancellationSource.CancelAfter((int)frame.Timeout);
                            }
                        }

                        Task messageTask;

                        var context = new LightweightCallContext(frame.Headers, cancellationSource?.Token ?? default);
                        scope = this.server.ServiceProvider?.CreateScope();

                        switch (frame.FrameType)
                        {
                            case RpcFrameType.UnaryRequest:
                                messageTask = methodStub.HandleMessage(this, frame, scope?.ServiceProvider, context);
                                break;
                            case RpcFrameType.StreamingRequest:
                                messageTask = methodStub.HandleStreamingMessage(this, frame, scope?.ServiceProvider, context);
                                break;
                            default:
                                throw new NotImplementedException();
                        }

                        if (messageTask.Status != TaskStatus.RanToCompletion)
                        {
                            var activeScope = scope;
                            activeOperation = this.CreateActiveOperation(frame.MessageNumber, activeOperation, cancellationSource);

                            // Make sure that the scope and cancellationSource are not disposed until the operation is finished.
                            scope = null;
                            cancellationSource = null;
                            return this.HandleAsyncOperation(frame, activeOperation, activeScope, messageTask);
                        }
                    }
                    else
                    {
                        throw new RpcFailureException(RpcFailure.RemoteDefinitionError, $"Unknown RPC operation '{frame.RpcOperation}'.");
                    }
                }
                catch (Exception e)
                {
                    // If it gets here, then a synchronous exception has been thrown,
                    // Let's handle it as if an incomplete task was returned.
                    activeOperation = this.CreateActiveOperation(frame.MessageNumber, activeOperation, cancellationSource);
                    var activeScope = scope;

                    // Make sure that the scope and cancellationSource are not disposed until the operation is finished.
                    scope = null;
                    cancellationSource = null;

                    return this.HandleAsyncOperation(frame, activeOperation, activeScope, Task.FromException(e));
                }
                finally
                {
                    scope?.Dispose();
                    cancellationSource?.Dispose();
                }

                // Return default task to allow receive loop to continue.
                return default;
            }

            private void RemoveActiveOperation(int messageId)
            {
                lock (this.syncRoot)
                {
                    this.activeOperations.Remove(messageId);
                }
            }

            private async Task WriteCancelResponseAsync(int messageId, string operationName)
            {
                // TODO: Should any headers be returned?
                ImmutableArray<KeyValuePair<string, string>> headers = ImmutableArray<KeyValuePair<string, string>>.Empty;
                var cancelFrame = new LightweightRpcFrame(RpcFrameType.CancelResponse, messageId, operationName, headers);

                await this.BeginWriteAsync(cancelFrame).ContextFree();
                await this.EndWriteAsync().ContextFree();
            }

            private Task WriteErrorResponseAsync(int messageId, string operationName, Exception e)
            {
                if (e is OperationCanceledException)
                {
                    return this.WriteCancelResponseAsync(messageId, operationName);
                }
                else if (e is RpcFailureException rfe)
                {
                    return this.WriteErrorResponseAsync(messageId, operationName, rfe.Message, rfe.Failure);
                }
                else
                {
                    // TODO: Implement IncludeExceptionDetailInFaults
                    string message = "The server was unable to process the request due to an internal error. "
                        + "For more information about the error, turn on IncludeExceptionDetailInFaults to send the exception information back to the client.";
                    return this.WriteErrorResponseAsync(messageId, operationName, message, RpcFailure.Unknown);
                }
            }

            private async Task WriteErrorResponseAsync(int messageId, string operationName, string message, RpcFailure failure)
            {
                // TODO: Should any additional headers be returned?
                var headers = new KeyValuePair<string, string>[]
                {
                    new KeyValuePair<string, string>(LightweightRpcFrame.ErrorMessageHeaderKey, message),
                    new KeyValuePair<string, string>(LightweightRpcFrame.ErrorCodeHeaderKey, failure.ToString())
                };

                var errorFrame = new LightweightRpcFrame(RpcFrameType.ErrorResponse, messageId, operationName, headers);

                await this.BeginWriteAsync(errorFrame).ContextFree();
                await this.EndWriteAsync().ContextFree();
            }

            private async Task WriteTimeoutResponseAsync(int messageId, string operationName)
            {
                // TODO: Should any headers be returned?
                ImmutableArray<KeyValuePair<string, string>> headers = ImmutableArray<KeyValuePair<string, string>>.Empty;
                var cancelFrame = new LightweightRpcFrame(RpcFrameType.TimeoutResponse, messageId, operationName, headers);

                await this.BeginWriteAsync(cancelFrame).ContextFree();
                await this.EndWriteAsync().ContextFree();
            }

            private sealed class ActiveOperation
            {
                internal ActiveOperation(int messageId, CancellationTokenSource? cancellationSource)
                {
                    this.MessageId = messageId;
                    this.CancellationSource = cancellationSource;
                }

                internal bool IsCancellationRequested { get; private set; }

                internal int MessageId { get; }

                private CancellationTokenSource? CancellationSource { get; }

                internal void Cancel()
                {
                    this.IsCancellationRequested = true;
                    this.CancellationSource?.Cancel();
                }
            }
        }
#pragma warning restore CA1031 // Do not catch general exception types
    }
}
