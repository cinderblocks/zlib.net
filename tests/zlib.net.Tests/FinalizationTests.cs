using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ComponentAce.Compression.Libs.zlib;
using Xunit;

namespace zlib.net.Tests
{
    public class FinalizationTests
    {
        private sealed class FailingSink : MemoryStream
        {
            public bool FailWrite, FailFlush, Disposed;
            public override void Write(byte[] buffer, int offset, int count)
            {
                if (FailWrite) throw new IOException("write failed");
                base.Write(buffer, offset, count);
            }
            public override void Flush() { if (FailFlush) throw new IOException("flush failed"); }
            protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
#if NET8_0_OR_GREATER
            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
            {
                if (FailWrite) throw new IOException("write failed");
                return base.WriteAsync(buffer, ct);
            }
#endif
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FinishPropagatesOutputFailures(bool flush)
        {
            var sink = new FailingSink { FailFlush = flush, FailWrite = !flush };
            var z = new ZOutputStream(sink, 6);
            Assert.Throws<IOException>(() => z.Finish());
            z.Dispose(); // A failed operation is not retried during cleanup.
            Assert.True(sink.Disposed);
            Assert.False(z.CanWrite);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void DisposePropagatesFailuresAndReleasesResources(bool flush, bool leaveOpen)
        {
            var sink = new FailingSink { FailFlush = flush, FailWrite = !flush };
            var z = new ZOutputStream(sink, 6, leaveOpen);
            Assert.Throws<IOException>(() => z.Dispose());
            Assert.Equal(!leaveOpen, sink.Disposed);
            Assert.False(z.CanWrite);
            z.Dispose();
            Assert.Throws<ObjectDisposedException>(() => z.Write(new byte[1], 0, 1));
        }

        [Fact]
        public void DisposeReportsTruncatedCompressedData()
        {
            var z = new ZOutputStream(new MemoryStream(), true);
            var input = CompletionTests.Pack(new byte[] { 1, 2, 3 });
            z.Write(input, 0, input.Length - 1);
            Assert.Throws<ZStreamException>(() => z.Dispose());
            z.Dispose();
        }

#if NET8_0_OR_GREATER && !LIB_NETSTANDARD20
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task AsyncFinalizationPropagatesFailures(bool flush)
        {
            var sink = new FailingSink { FailFlush = flush, FailWrite = !flush };
            var z = new ZOutputStream(sink, 6);
            await Assert.ThrowsAsync<IOException>(async () => await z.FinishAsync());
            await z.DisposeAsync();
            Assert.True(sink.Disposed);

            sink = new FailingSink { FailFlush = flush, FailWrite = !flush };
            z = new ZOutputStream(sink, 6);
            await Assert.ThrowsAsync<IOException>(async () => await z.DisposeAsync());
            Assert.True(sink.Disposed);
            Assert.False(z.CanWrite);
            await z.DisposeAsync();
        }

        [Fact]
        public async Task CanceledFinishCanBeRetriedBeforeAnyWork()
        {
            var sink = new MemoryStream();
            var z = new ZOutputStream(sink, 6, true);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await z.FinishAsync(new CancellationToken(true)));
            await z.FinishAsync();
            Assert.NotEmpty(sink.ToArray());
            await z.DisposeAsync();
        }
#endif
    }
}
