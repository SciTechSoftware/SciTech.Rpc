using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;

namespace SciTech.Rpc.Lightweight.Internal
{
    internal sealed class StreamDuplexPipe : IDuplexPipe, IDisposable, IAsyncDisposable
    {
        Stream stream;

        internal StreamDuplexPipe(Stream stream)
        {
            this.stream = stream;
            this.Input = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
            this.Output = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));
        }

        public PipeReader Input { get; }

        public PipeWriter Output { get; }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    this.Input.Complete();
                    this.Output.Complete();
                }
                finally
                {
                    // I don't expect any exceptions in Complete, but let's make sure the stream is disposed.
                    this.stream.Dispose();
                }

            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await this.Input.CompleteAsync().ContextFree();
                await this.Output.CompleteAsync().ContextFree();
            }
            finally
            {
                // I don't expect any exceptions in CompleteAsync, but let's make sure the stream is disposed.
#if PLAT_ASYNC_DISPOSE
                await this.stream.DisposeAsync().ContextFree();
#else
                this.stream.Dispose();
#endif
            }
        }

       public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
