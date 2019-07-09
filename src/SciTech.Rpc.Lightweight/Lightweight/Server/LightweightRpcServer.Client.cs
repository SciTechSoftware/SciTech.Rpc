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
        private class Client : RpcPipeline
        {
            private readonly Dictionary<int, ActiveOperation> activeOperations = new Dictionary<int, ActiveOperation>();

            private readonly LightweightRpcServer server;

            private readonly object syncRoot = new object();

            private IRpcSerializer serializer;

            public Client(IDuplexPipe pipe, LightweightRpcServer server, int? maxRequestSize = null, int? maxResponseSize = null) : base(pipe, maxResponseSize, maxRequestSize)
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
                    activeOp.CancellationSource?.Cancel();
                }
            }

            protected override ValueTask OnEndReceiveLoopAsync()
            {
                this.server.RemoveClient(this);
                this.Close();
                return default;
            }

            protected sealed override ValueTask OnReceiveAsync(in LightweightRpcFrame frame)
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
                        handleRequestTask = this.HandleCancelRequestAsync(frame);
                        break;
                    default:
                        throw new NotImplementedException();
                }

                return handleRequestTask;
            }

            protected override void OnReceiveLoopFaulted(Exception e)
            {
                this.server.RemoveClient(this);
                this.Close(e);
            }

            protected override ValueTask OnStartReceiveLoopAsync()
            {
                this.server.AddClient(this);
                return default;
            }

            private void AddActiveOperation(ActiveOperation activeOperation)
            {
                lock (this.syncRoot)
                {
                    this.activeOperations.Add(activeOperation.MessageId, activeOperation);
                }
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
            private ValueTask HandleAsyncOperation(in LightweightRpcFrame frame, ActiveOperation? activeOperation, IServiceScope? scope, Task messageTask)
            {
                // TODO: Throttle the number of active operations.

                int messageId = frame.MessageNumber;
                string operationName = frame.RpcOperation;

                if (activeOperation == null)
                {
                    activeOperation = new ActiveOperation(messageId, null);
                    this.AddActiveOperation(activeOperation);
                }

                async void HandleCompletedAsyncOperation(Task t)
                {
                    try
                    {

                        if (t.IsCanceled)
                        {
                            await this.WriteCancelResponseAsync(messageId, operationName).ContextFree();
                        }
                        else if (t.IsFaulted)
                        {
                            // Note. It will currently get here if the operation handler failed to 
                            // write the response. Most likely we will not succeed now either (and the 
                            // pipe will be closed). Maybe the handler should close the pipe instead 
                            // and not propagate the error?
                            await this.WriteErrorResponseAsync(messageId, operationName, t.Exception).ContextFree();
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

            private ValueTask HandleCancelRequestAsync(in LightweightRpcFrame frame)
            {
                ActiveOperation? activeOperation;
                lock (this.syncRoot)
                {
                    this.activeOperations.TryGetValue(frame.MessageNumber, out activeOperation);
                }

                activeOperation?.CancellationSource?.Cancel();

                return default;
            }
            //private ValueTask HandleStreamingRequestAsync(in LightweightRpcFrame frame)
            //{
            //    var methodStub = this._server.GetMethodDefinition(frame.RpcOperation);
            //    if (methodStub != null)
            //    {
            //        var context = new LightweightCallContext(frame.Headers, CancellationToken.None);
            //        var streamingTask = methodStub.HandleStreamingMessage(this, frame, context);
            //        // TODO: Throttle streaming tasks?
            //        return default;
            //    }
            //    else
            //    {
            //        // TODO: Create error response (unknown operation)
            //        throw new NotImplementedException();
            //    }
            //}

            private ValueTask HandleRequestAsync(in LightweightRpcFrame frame)
            {
                IServiceScope? scope = null;
                ActiveOperation? activeOperation = null;
                try
                {
                    var methodStub = this.server.GetMethodDefinition(frame.RpcOperation);
                    if (methodStub != null)
                    {
                        bool canCancel = (frame.OperationFlags & RpcOperationFlags.CanCancel) != 0;
                        CancellationTokenSource? cancellationSource = null;

                        if (canCancel)
                        {
                            cancellationSource = new CancellationTokenSource();
                            activeOperation = new ActiveOperation(frame.MessageNumber, cancellationSource);

                            this.AddActiveOperation(activeOperation);
                        }

                        ValueTask messageTask;

                        var context = new LightweightCallContext(frame.Headers, cancellationSource?.Token ?? default);
                        scope = this.server.ServiceProvider?.CreateScope();
                        // TODO: HandleMessage should be a virtual method and not based on FrameType switch.
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

                        if (!messageTask.IsCompletedSuccessfully)
                        {
                            var activeScope = scope;
                            // Make sure that the scope is not disposed until the operation is finished.
                            scope = null;
                            return this.HandleAsyncOperation(frame, activeOperation, activeScope, messageTask.AsTask());
                        }
                    }
                    else
                    {
                        throw new RpcFailureException($"Unknown RPC operation '{frame.RpcOperation}'.");
                    }
                }
                catch (Exception e)
                {
                    // If it gets here, then a synchronous exception has been thrown,
                    // Let's handle it as if an incomplete task was returned.
                    return this.HandleAsyncOperation(frame, null, null, Task.FromException(e));
                }
                finally
                {
                    scope?.Dispose();
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
                // TODO: Should any headers be returned.
                ImmutableArray<KeyValuePair<string, string>> headers = ImmutableArray<KeyValuePair<string, string>>.Empty;
                var cancelFrame = new LightweightRpcFrame(RpcFrameType.CancelResponse, messageId, operationName, headers);

                await this.BeginWriteAsync(cancelFrame).ContextFree();
                await this.EndWriteAsync().ContextFree();
            }

            private async Task WriteErrorResponseAsync(int messageId, string operationName, string message)
            {
                // TODO: Add exception message in header?
                ImmutableArray<KeyValuePair<string, string>> headers = ImmutableArray<KeyValuePair<string, string>>.Empty;
                var errorFrame = new LightweightRpcFrame(RpcFrameType.ErrorResponse, messageId, operationName, headers);

                await this.BeginWriteAsync(errorFrame).ContextFree();
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
                    return this.WriteErrorResponseAsync(messageId, operationName, rfe.Message);
                }
                else
                {
                    // TODO: Implement IncludeExceptionDetailInFaults
                    string message = "The server was unable to process the request due to an internal error. "
                        + "For more information about the error, turn on IncludeExceptionDetailInFaults to send the exception information back to the client.";
                    return this.WriteErrorResponseAsync(messageId, operationName, message);
                }
            }

            private sealed class ActiveOperation
            {
                internal ActiveOperation(int messageId, CancellationTokenSource? cancellationSource)
                {
                    this.MessageId = messageId;
                    this.CancellationSource = cancellationSource;
                }

                internal CancellationTokenSource? CancellationSource { get; }

                internal int MessageId { get; }
            }
        }
#pragma warning restore CA1031 // Do not catch general exception types
    }
}
