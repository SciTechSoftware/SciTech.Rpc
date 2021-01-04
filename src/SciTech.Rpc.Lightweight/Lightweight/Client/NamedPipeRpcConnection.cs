using Pipelines.Sockets.Unofficial;
using SciTech.Rpc.Client;
using SciTech.Rpc.Lightweight.Client.Internal;
using SciTech.Rpc.Lightweight.Internal;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO.Pipelines;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Client
{
    public class NamedPipeRpcConnection : LightweightRpcConnection
    {
        public NamedPipeRpcConnection(
            RpcServerConnectionInfo connectionInfo,
            IRpcClientOptions? options = null,
            LightweightOptions? lightweightOptions = null)
            : this(connectionInfo, options,
                  LightweightProxyGenerator.Default,
                  lightweightOptions)
        {
        }
        
        public NamedPipeRpcConnection(
            string path,
            IRpcClientOptions? options = null,            
            LightweightOptions? lightweightOptions = null)
            : this( 
                  new RpcServerConnectionInfo( new Uri($"{WellKnownRpcSchemes.LightweightPipe}://./{path}" )), 
                  options,
                  LightweightProxyGenerator.Default,
                  lightweightOptions)
        {
        }

        internal NamedPipeRpcConnection(
            RpcServerConnectionInfo connectionInfo,
            IRpcClientOptions? options,
            LightweightProxyGenerator proxyGenerator,
            LightweightOptions? lightweightOptions)
            : base(connectionInfo, options,
                  proxyGenerator,
                  lightweightOptions)
        {
        }

        public override bool IsEncrypted => false;

        public override bool IsMutuallyAuthenticated => false;

        public override bool IsSigned => false;

        [EditorBrowsable(EditorBrowsableState.Never)]
        protected override async Task<IDuplexPipe> ConnectPipelineAsync(int sendMaxMessageSize, int receiveMaxMessageSize, CancellationToken cancellationToken)
        {
            // TODO: The URL should be parsed in RpConnectionInfo constructor .
            // If invalid an ArgumentException should be thrown there.

            if (this.ConnectionInfo.HostUrl is Uri url)
            {
#pragma warning disable CA2000 // Dispose objects before losing scope
                string pipeName = PipeUri.LookupPipeName(url);
                if( string.IsNullOrEmpty(pipeName ))
                {
                    throw new RpcCommunicationException(RpcCommunicationStatus.Unavailable, $"Failed to connect to named pipe at '{url}'");
                }

                NamedPipeClientStream? pipeClientStream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);

                try
                {
                    await pipeClientStream.ConnectAsync(cancellationToken).ContextFree();

                    var sendOptions = new System.IO.Pipelines.PipeOptions(
                        pauseWriterThreshold: sendMaxMessageSize * 2, resumeWriterThreshold: sendMaxMessageSize,
                        readerScheduler: System.IO.Pipelines.PipeScheduler.Inline,
                        useSynchronizationContext: false);
                    var receiveOptions = new System.IO.Pipelines.PipeOptions(
                        pauseWriterThreshold: receiveMaxMessageSize * 2, resumeWriterThreshold: receiveMaxMessageSize,
                        readerScheduler: System.IO.Pipelines.PipeScheduler.Inline,
                        useSynchronizationContext: false);

                    var connection = StreamConnection.GetDuplex(pipeClientStream, sendOptions, receiveOptions);
                    if (!(connection is IDisposable))
                    {
                        // Rather dummy, we need to dispose the stream when pipe is disposed, but
                        // this is not performed by the pipe returned by StreamConnection.
                        connection = new OwnerDuplexPipe(connection, pipeClientStream);
                    }

                    pipeClientStream = null;    // Prevent disposal

                    return connection;
                }
                finally
                {
                    pipeClientStream?.Dispose();
                }
#pragma warning restore CA2000 // Dispose objects before losing scope
            } else
            {
                throw new InvalidOperationException("Missing connection URL.");
            }
        }

        private static string GetComputerFromUrl(Uri url)
        {
            if( url.IsLoopback || string.IsNullOrEmpty(url.Host) || url.Host == "." )
            {
                return ".";
            }

            return url.Host;
        }
    }
}
