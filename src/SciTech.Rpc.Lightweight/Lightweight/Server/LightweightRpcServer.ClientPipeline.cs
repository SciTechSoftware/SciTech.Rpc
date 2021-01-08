#region Copyright notice and license

// Copyright (c) 2019, SciTech Software AB
// All rights reserved.
//
// Licensed under the BSD 3-Clause License.
// You may obtain a copy of the License at:
//
//     https://github.com/SciTechSoftware/SciTech.Rpc/blob/master/LICENSE
//

#endregion Copyright notice and license

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SciTech.Rpc.Client;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Lightweight.Server.Internal;
using SciTech.Rpc.Serialization;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server
{
    public partial class LightweightRpcServer
    {
        /// <summary>
        /// Handles the communication with a connected client.
        /// </summary>
        private class ClientPipeline : RpcPipeline
        {
            private static readonly Action<ILogger, string, Exception?> LogBeginHandleStreamingRequest = LoggerMessage.Define<string>(LogLevel.Information,
                new EventId(1, "BeginHandleStreamingRequest"),
                "Begin handle streaming request '{Operation}'.");

            private static readonly Action<ILogger, string, Exception?> LogBeginHandleUnaryRequest = LoggerMessage.Define<string>(LogLevel.Information,
                new EventId(2, "BeginHandleUnaryRequest"),
                "Begin handle unary request '{Operation}'.");

            private static readonly Action<ILogger, string, Exception?> LogRequestCancelled = LoggerMessage.Define<string>(LogLevel.Information,
                new EventId(3, "RequestCancelled"),
                "Request '{Operation}' was cancelled.");

            private static readonly Action<ILogger, string, Exception?> LogRequestError = LoggerMessage.Define<string>(LogLevel.Information,
                new EventId(4, "RequestError"),
                "Request '{Operation}' threw exception.");

            private static readonly Action<ILogger, string, Exception?> LogRequestFailed = LoggerMessage.Define<string>(LogLevel.Warning,
                new EventId(5, "RequestFailed"),
                "Request '{Operation}' failed.");

            private static readonly Action<ILogger, string, Exception?> LogRequestHandledAsynchronously = LoggerMessage.Define<string>(LogLevel.Information,
                new EventId(6, "RequestHandledAsynchronously"),
                "Request '{Operation}' handled asynchronously.");

            private static readonly Action<ILogger, string, Exception?> LogRequestHandledSynchronously = LoggerMessage.Define<string>(LogLevel.Information,
                new EventId(7, "RequestHandledSynchronously"),
                "Request '{Operation}' handled synchronously.");

            private static readonly Action<ILogger, string, Exception?> LogRequestTimedOut = LoggerMessage.Define<string>(LogLevel.Information,
                new EventId(8, "RequestTimedOut"),
                "Request '{Operation}' timed out.");

            private static readonly Action<ILogger, string,int,int, Exception?> LogReceivedLargeFrame = LoggerMessage.Define<string,int,int>(LogLevel.Warning,
                new EventId(9, "ReceivedLargeFrame"),
                "Size of received request frame for operation '{Operation}' exceeds size limit(frame size={FrameLength}, max size={MaxReceiveFrameLength}).");

            private readonly Dictionary<int, ActiveOperation> activeOperations = new Dictionary<int, ActiveOperation>();

            private readonly LightweightRpcServer server;

            private readonly object syncRoot = new object();
            private readonly IPrincipal? user;
            private readonly LightweightRpcEndPoint endPoint;
            private readonly IRpcSerializer serializer;

            internal ClientPipeline(IDuplexPipe pipe, LightweightRpcServer server, LightweightRpcEndPoint endPoint, IPrincipal? user, int? maxRequestSize, int? maxResponseSize, bool skipLargeFrames)
                : base(pipe, maxResponseSize, maxRequestSize, skipLargeFrames)
            {
                this.server = server;
                this.endPoint = endPoint;
                this.user = user;
                this.serializer = server.Serializer;
            }

            public Task RunAsync() => this.StartReceiveLoopAsync();

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
                LogReceivedLargeFrame(this.server.Logger, frame.RpcOperation, frame.FrameLength ?? 0, this.MaxReceiveFrameLength, null);

                string msg = $"Size of received request frame exceeds size limit (frame size={frame.FrameLength}, max size={this.MaxReceiveFrameLength}).";

                return this.WriteFailureResponseAsync(frame.MessageNumber, "", msg, RpcFailure.SizeLimitExceeded, null);
            }

            protected override async Task OnReceiveLoopFaultedAsync(ExceptionEventArgs e)
            {
                if( e.Exception is RpcCommunicationException rce && rce.Status == RpcCommunicationStatus.ConnectionLost)
                {
                    // A connection lost exception is expected when a client disconnects.
                    // Log: Client disconnecteed
                }
                else
                {
                    // Log warning.
                }

                await base.OnReceiveLoopFaultedAsync(e).ContextFree();

                await this.CloseAsync(e.Exception).ContextFree();
                this.server.RemoveClient(this);
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
            [SuppressMessage("Design", "CA1031:Do not catch general exception types")]
            private ValueTask HandleAsyncOperation(
                in LightweightRpcFrame frame, ActiveOperation activeOperation, IServiceScope? scope, IRpcSerializer? serializer, Task messageTask)
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
                                LogRequestCancelled(this.server.Logger, operationName, null);
                                await this.WriteCancelResponseAsync(messageId, operationName).ContextFree();
                            }
                            else
                            {
                                // If the operation is cancelled, but cancellation has not been requested, then
                                // it's actually a timeout.
                                LogRequestTimedOut(this.server.Logger, operationName, null);
                                await this.WriteTimeoutResponseAsync(messageId, operationName).ContextFree();
                            }
                        }
                        else if (t.IsFaulted)
                        {
                            // Note. It will currently get here if the operation handler failed to
                            // write the response as well. Most likely we will not succeed now either (and the
                            // pipe will be closed). Maybe the handler should close the pipe instead
                            // and not propagate the error?
                            LogRequestError(this.server.Logger, operationName, t.Exception!.InnerException ?? t.Exception);
                            await this.WriteExceptionResponseAsync(messageId, operationName, t.Exception!.InnerException ?? t.Exception, serializer).ContextFree();
                        }
                    }
                    catch (Exception e)
                    {
                        LogRequestFailed(this.server.Logger, operationName, e);
                        await this.CloseAsync(e).ContextFree();  // Will not throw.
                    }
                    finally
                    {
                        this.RemoveActiveOperation(messageId);
                        scope?.Dispose();
                        LogRequestHandledAsynchronously(this.server.Logger, operationName, null);
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

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types")]
            private ValueTask HandleRequestAsync(in LightweightRpcFrame frame)
            {
                IServiceScope? scope = null;
                IDisposable? logScope = null;
                ActiveOperation? activeOperation = null;
                CancellationTokenSource? cancellationSource = null;
                LightweightMethodStub? methodStub = null;
                try
                {
                    methodStub = this.server.GetMethodDefinition(frame.RpcOperation);
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

                        var context = new LightweightCallContext(this.endPoint, this.user, frame.Headers, cancellationSource?.Token ?? default);
                        scope = this.server.ServiceProvider?.CreateScope();

                        switch (frame.FrameType)
                        {
                            case RpcFrameType.UnaryRequest:
                                LogBeginHandleUnaryRequest(this.server.Logger, frame.RpcOperation, null);

                                messageTask = methodStub.HandleMessage(this, frame, scope?.ServiceProvider, context);
                                break;

                            case RpcFrameType.StreamingRequest:
                                LogBeginHandleStreamingRequest(this.server.Logger, frame.RpcOperation, null);

                                messageTask = methodStub.HandleStreamingMessage(this, frame, scope?.ServiceProvider, context);
                                break;

                            default:
                                throw new NotImplementedException();
                        }

                        if (messageTask.Status != TaskStatus.RanToCompletion)
                        {
                            var activeScope = scope;
                            var activeLogScope = logScope;
                            activeOperation = this.CreateActiveOperation(frame.MessageNumber, activeOperation, cancellationSource);

                            // Make sure that the scope and cancellationSource are not disposed until the operation is finished.
                            scope = null;
                            logScope = null;
                            cancellationSource = null;
                            return this.HandleAsyncOperation(frame, activeOperation, activeScope, methodStub.Serializer, messageTask);
                        }
                        else
                        {
                            LogRequestHandledSynchronously(this.server.Logger, frame.RpcOperation, null);
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
                    var activeLogScope = logScope;

                    // Make sure that the scope and cancellationSource are not disposed until the operation is finished.
                    scope = null;
                    logScope = null;
                    cancellationSource = null;

                    return this.HandleAsyncOperation(frame, activeOperation, activeScope, methodStub?.Serializer, Task.FromException(e));
                }
                finally
                {
                    scope?.Dispose();
                    logScope?.Dispose();
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
                ImmutableArray<KeyValuePair<string, ImmutableArray<byte>>> headers = ImmutableArray<KeyValuePair<string, ImmutableArray<byte>>>.Empty;
                var cancelFrame = new LightweightRpcFrame(RpcFrameType.CancelResponse, messageId, operationName, headers);

                var writeState = this.BeginWrite(cancelFrame);
                await this.EndWriteAsync(writeState, false).ContextFree();
            }

            private async Task WriteErrorResponseAsync(int messageId, string operationName, RpcError rpcError, IRpcSerializer? serializer)
            {
                //// TODO: Should any additional headers be returned?
                List<KeyValuePair<string, ImmutableArray<byte>>> headers;
                if (serializer != null)
                {
                    headers = new List<KeyValuePair<string, ImmutableArray<byte>>>
                    {
                        new KeyValuePair<string, ImmutableArray<byte>>(WellKnownHeaderKeys.ErrorInfo, serializer.Serialize(rpcError).ToImmutableArray())
                    };
                }
                else
                {
                    headers = new List<KeyValuePair<string, ImmutableArray<byte>>>
                    {
                        new KeyValuePair<string, ImmutableArray<byte>>(WellKnownHeaderKeys.ErrorType, RpcRequestContext.ToHeaderBytes(rpcError.ErrorType)),
                        new KeyValuePair<string, ImmutableArray<byte>>(WellKnownHeaderKeys.ErrorCode, RpcRequestContext.ToHeaderBytes(rpcError.ErrorCode)),
                        new KeyValuePair<string, ImmutableArray<byte>>(WellKnownHeaderKeys.ErrorMessage , RpcRequestContext.ToHeaderBytes(rpcError.Message))
                    };

                    if (rpcError.ErrorDetails != null)
                    {
                        headers.Add(new KeyValuePair<string, ImmutableArray<byte>>(WellKnownHeaderKeys.ErrorMessage, rpcError.ErrorDetails.ToImmutableArray()));
                    }
                }

                var errorFrame = new LightweightRpcFrame(RpcFrameType.ErrorResponse, messageId, operationName, headers);

                var writeState = this.BeginWrite(errorFrame);
                await this.EndWriteAsync(writeState, false).ContextFree();
            }

            private Task WriteExceptionResponseAsync(int messageId, string operationName, Exception e, IRpcSerializer? serializer)
            {
                RpcError? rpcError = RpcError.TryCreate(e, serializer);
                if (rpcError != null)
                {
                    return this.WriteErrorResponseAsync(messageId, operationName, rpcError, serializer);
                }

                if (e is OperationCanceledException)
                {
                    return this.WriteCancelResponseAsync(messageId, operationName);
                }
                else
                {
                    // TODO: Implement IncludeExceptionDetailInFaults
                    string message = $"The server was unable to process the request for operation '{operationName}' due to an internal error. "
                        + "For more information about the error, turn on IncludeExceptionDetailInFaults to send the exception information back to the client.";
                    return this.WriteFailureResponseAsync(messageId, operationName, message, RpcFailure.Unknown, serializer);
                }
            }

            private Task WriteFailureResponseAsync(int messageId, string operationName, string message, RpcFailure failure, IRpcSerializer? serializer)
            {
                RpcError rpcError = new RpcError
                {
                    ErrorType = WellKnownRpcErrors.Failure,
                    ErrorCode = failure.ToString(),
                    Message = message
                };

                return WriteErrorResponseAsync(messageId, operationName, rpcError, serializer);
            }

            private async Task WriteTimeoutResponseAsync(int messageId, string operationName)
            {
                // TODO: Should any headers be returned?
                ImmutableArray<KeyValuePair<string, ImmutableArray<byte>>> headers = ImmutableArray<KeyValuePair<string, ImmutableArray<byte>>>.Empty;
                var cancelFrame = new LightweightRpcFrame(RpcFrameType.TimeoutResponse, messageId, operationName, headers);

                var writeState = this.BeginWrite(cancelFrame);
                await this.EndWriteAsync(writeState, false).ContextFree();
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
    }
}