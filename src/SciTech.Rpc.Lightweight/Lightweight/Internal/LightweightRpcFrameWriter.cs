﻿using SciTech.Rpc.Serialization;
using SciTech.Rpc.Serialization.Internal;
using System;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Internal
{
    internal class LightweightRpcFrameWriter : ILightweightRpcFrameWriter, IDisposable
    {
        private BufferWriterStream writer = new BufferWriterStreamImpl();
        private int maxFrameSize;

        private bool isWriting;

        private bool hasFrameData;

        public LightweightRpcFrameWriter(int maxFrameSize)
        {
            this.maxFrameSize = maxFrameSize;
        }

        private void AbortWrite()
        {
            this.isWriting = this.hasFrameData = false;
        }

        void ILightweightRpcFrameWriter.AbortWrite(in LightweightRpcFrame.WriteState state)
            => this.AbortWrite();


        internal byte[] WriteFrame<T>(in LightweightRpcFrame frameHeader, T payload, IRpcSerializer serializer)
        {
            var state = this.BeginWrite(frameHeader);
            try
            {
                serializer.Serialize(state.Writer, payload, typeof(T));
                this.EndWrite(state);
                return this.GetFrameData()!; 
            }
            catch
            {
                this.AbortWrite();
                throw;
            }
        }

        internal byte[] WriteFrame(in LightweightRpcFrame frameHeader)
        {
            var state = this.BeginWrite(frameHeader);
            try
            {
                this.EndWrite(state);
                return this.GetFrameData()!;
            }
            catch
            {
                this.AbortWrite();
                throw;
            }
        }

        private LightweightRpcFrame.WriteState BeginWrite(in LightweightRpcFrame responseHeader)
        {
            if (this.isWriting) throw new InvalidOperationException("Already writing in LightweightRpcFrameWriter.");

            this.isWriting = true;
            this.hasFrameData = false;
            this.writer.Reset();
            return responseHeader.BeginWrite(this.writer);
        }

        LightweightRpcFrame.WriteState ILightweightRpcFrameWriter.BeginWrite(in LightweightRpcFrame responseHeader)
            => this.BeginWrite(responseHeader);

        private void EndWrite(in LightweightRpcFrame.WriteState state)
        {
            if (!this.isWriting) throw new InvalidOperationException("EndWriteAsync called without a BeginWriteCall.");

            LightweightRpcFrame.EndWrite((int)this.writer.Length, state);
            this.isWriting = false;
            this.hasFrameData = true;
        }

        ValueTask ILightweightRpcFrameWriter.EndWriteAsync(in LightweightRpcFrame.WriteState state, bool throwOnError)
        {
            this.EndWrite(state);
            return default;
        }

        internal void Reset()
        {
            this.isWriting = this.hasFrameData = false;
            this.writer.Reset();
        }

        internal byte[]? GetFrameData()
        {
            return this.hasFrameData ? this.writer.ToArray() : null;
        }

        public void Dispose()
        {
            this.Reset();
            this.writer.Dispose();
        }
    }


}
