using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComponentAce.Compression.Libs.zlib;
using Xunit;
namespace zlib.net.Tests
{
    public class FlushTests
    {
        private static byte[] DecodePrefix(byte[] packed)
        {
            var z = new ZStream(); z.inflateInit(); z.next_in = packed; z.avail_in = packed.Length;
            z.next_out = new byte[4096]; z.avail_out = z.next_out.Length;
            try { int status = z.inflate(0); Assert.True(status == 0 || status == 1); return z.next_out.Take(z.next_out_index).ToArray(); }
            finally { z.inflateEnd(); }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(7)]
        [InlineData(16384)]
        public void FlushMakesMessagesReadableAndAllowsContinuedWrites(int size)
        {
            var target = new MemoryStream(); using var z = new ZOutputStream(target, 6, true, size);
            byte[] first = { 1, 2, 3, 4, 5, 6, 7 }, second = { 8, 9, 10 };
            z.Write(first, 0, first.Length); z.Flush();
            Assert.Equal(first, DecodePrefix(target.ToArray()));
            long length = target.Length; z.Flush(); Assert.Equal(length, target.Length);
            z.Write(second, 0, second.Length); z.Flush();
            Assert.Equal(first.Concat(second).ToArray(), DecodePrefix(target.ToArray()));
            z.Finish(); length = target.Length; z.Flush(); z.Finish(); Assert.Equal(length, target.Length);
        }

#if NET8_0_OR_GREATER && !LIB_NETSTANDARD20
        private sealed class AsyncSink : MemoryStream
        {
            public override void Write(byte[] b, int o, int n) => throw new Exception("Sync write");
            public override void Flush() => throw new Exception("Sync flush");
            public override Task WriteAsync(byte[] b, int o, int n, CancellationToken ct) { base.Write(b, o, n); return Task.CompletedTask; }
            public override ValueTask WriteAsync(ReadOnlyMemory<byte> b, CancellationToken ct = default) { var bytes = b.ToArray(); base.Write(bytes, 0, bytes.Length); return default; }
            public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
        }
        [Theory]
        [InlineData(1)]
        [InlineData(16384)]
        public async Task AsyncFlushOnlyUsesAsyncSinkOperations(int size)
        {
            var sink = new AsyncSink(); var z = new ZOutputStream(sink, 6, true, size);
            byte[] message = { 1, 2, 3, 4, 5, 6, 7 };
            await z.WriteAsync(message.AsMemory()); await z.FlushAsync();
            Assert.Equal(message, DecodePrefix(sink.ToArray()));
            await z.FlushAsync(); await z.DisposeAsync();
        }

        [Fact]
        public async Task PreCanceledFlushLeavesStreamUsable()
        {
            var target = new MemoryStream(); var z = new ZOutputStream(target, 6, true);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => z.FlushAsync(new CancellationToken(true)));
            z.Write(new byte[] { 1 }, 0, 1); z.Flush(); await z.DisposeAsync();
        }
#endif
    }
}
