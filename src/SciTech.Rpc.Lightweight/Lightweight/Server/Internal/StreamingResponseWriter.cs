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

using SciTech.Rpc.Lightweight.Internal;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server.Internal;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Server.Internal
{
    internal class StreamingResponseWriter<TResponse> : IRpcAsyncStreamWriter<TResponse>
    {
        private int messageNumber;

        private RpcPipeline pipelineClient;

        private string rpcOperation;

        private IRpcSerializer<TResponse> serializer;

        public StreamingResponseWriter(RpcPipeline pipelineClient, IRpcSerializer<TResponse> serializer, int messageNumber, string rpcOperation)
        {
            this.pipelineClient = pipelineClient ?? throw new ArgumentNullException(nameof(pipelineClient));
            this.serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            this.messageNumber = messageNumber;
            this.rpcOperation = rpcOperation ?? throw new ArgumentNullException(nameof(rpcOperation));
        }

        public async Task WriteAsync(TResponse response)
        {
            var responseHeader = new LightweightRpcFrame(
                RpcFrameType.StreamingResponse, this.messageNumber, this.rpcOperation,
                ImmutableArray<KeyValuePair<string, string>>.Empty);

            var responseStream = await this.pipelineClient.BeginWriteAsync(responseHeader).ContextFree();
            try
            {
                this.serializer.Serialize(responseStream, response);
            }
            catch
            {
                this.pipelineClient.AbortWrite();
                throw;
            }

            await this.pipelineClient.EndWriteAsync().ContextFree();
        }

        internal async Task EndAsync()
        {
            var responseHeader = new LightweightRpcFrame(
                RpcFrameType.StreamingEnd, this.messageNumber, this.rpcOperation,
                ImmutableArray<KeyValuePair<string, string>>.Empty);

            var responseStream = await this.pipelineClient.TryBeginWriteAsync(responseHeader).ContextFree();
            if (responseStream != null)
            {
                // Response data is ignored when frame is StreamingEnd,
                // and we have not suitable response to write anyway.
                await this.pipelineClient.EndWriteAsync().ContextFree();
            }
        }
    }
}
