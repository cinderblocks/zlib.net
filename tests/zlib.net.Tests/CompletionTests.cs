using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComponentAce.Compression.Libs.zlib;
using Xunit;

namespace zlib.net.Tests
{
    public class CompletionTests
    {
        internal static byte[] Pack(byte[] data)
        {
            var target = new MemoryStream();
            using (var z = new ZOutputStream(target, 6, true)) z.Write(data, 0, data.Length);
            return target.ToArray();
        }

        // Also bounds the old broken loop: it writes empty buffers indefinitely.
        private sealed class GuardSink : MemoryStream
        {
            private int calls;
            public override void Write(byte[] buffer, int offset, int count)
            {
                if (++calls > 100) throw new InvalidOperationException("Codec failed to terminate.");
                base.Write(buffer, offset, count);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TrailingDataIsRejectedWithoutLooping(bool concatenated)
        {
            var packed = Pack(new byte[] { 1, 2, 3 });
            var input = packed.Concat(concatenated ? packed : new byte[] { 42 }).ToArray();
            var z = new ZOutputStream(new GuardSink(), true);
            try { Assert.Throws<ZStreamException>(() => z.Write(input, 0, input.Length)); }
            finally { z.End(); z.Dispose(); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AdditionalWriteAfterEndIsRejected(bool compress)
        {
            var target = new MemoryStream();
            using var z = compress ? new ZOutputStream(target, 6, true) : new ZOutputStream(target, true);
            byte[] input = compress ? new byte[] { 1 } : Pack(new byte[] { 1 });
            z.Write(input, 0, input.Length);
            z.Finish();
            z.Finish();
            long length = target.Length;
            Assert.Throws<InvalidOperationException>(() => z.Write(new byte[] { 2 }, 0, 1));
            Assert.Equal(length, target.Length);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(4096)]
        [InlineData(32768)]
        public void FinishIsIdempotentAndPreservesExactPayload(int length)
        {
            var data = TestData.Random(length);
            var packed = Pack(data);
            var target = new MemoryStream();
            using (var z = new ZOutputStream(target, true))
            {
                z.Write(packed, 0, packed.Length);
                z.Finish(); z.Finish();
            }
            Assert.Equal(data, target.ToArray());
        }

#if NET8_0_OR_GREATER && !LIB_NETSTANDARD20
        [Fact]
        public async Task AsyncAndSpanWritesRejectTrailingData()
        {
            var packed = Pack(new byte[] { 1 }).Concat(new byte[] { 42 }).ToArray();
            var z = new ZOutputStream(new GuardSink(), true);
            try { await Assert.ThrowsAsync<ZStreamException>(() => z.WriteAsync(packed, 0, packed.Length, CancellationToken.None)); }
            finally { z.End(); await z.DisposeAsync(); }
            z = new ZOutputStream(new GuardSink(), true);
            try { Assert.Throws<ZStreamException>(() => z.Write(packed.AsSpan())); }
            finally { z.End(); z.Dispose(); }
            z = new ZOutputStream(new GuardSink(), true);
            try { await Assert.ThrowsAsync<ZStreamException>(async () => await z.WriteAsync(packed.AsMemory())); }
            finally { z.End(); await z.DisposeAsync(); }
        }

        [Fact]
        public async Task AsyncFinishIsIdempotent()
        {
            var target = new MemoryStream();
            var z = new ZOutputStream(target, 6, true);
            await z.WriteAsync(new byte[] { 1, 2, 3 }.AsMemory());
            await z.FinishAsync();
            long length = target.Length;
            await z.FinishAsync();
            await z.DisposeAsync();
            Assert.Equal(length, target.Length);
        }
#endif
    }
}
