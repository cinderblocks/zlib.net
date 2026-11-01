using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ComponentAce.Compression.Libs.zlib;
using Xunit;
namespace zlib.net.Tests
{
    public class BufferedReadTests
    {
        private sealed class WithheldTrailer : MemoryStream
        {
            private readonly int firstLength;
            private bool first = true;
            public WithheldTrailer(byte[] data) : base(data) { firstLength = data.Length - 4; }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (!first) { throw new IOException("Trailer has not arrived."); }
                first = false;
                return base.Read(buffer, offset, Math.Min(count, firstLength));
            }
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.FromResult(Read(buffer, offset, count));
#if NET8_0_OR_GREATER
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                var tmp = new byte[buffer.Length]; int n = Read(tmp, 0, tmp.Length); tmp.AsMemory(0, n).CopyTo(buffer); return new ValueTask<int>(n);
            }
#endif
        }

        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(4096)]
        public void BufferedDecodedOutputIsReturnedBeforeRefilling(int size)
        {
            byte[] data = new byte[50000];
            using var z = new ZInputStream(new WithheldTrailer(CompletionTests.Pack(data)));
            var buffer = new byte[size]; int total = 0;
            while (total < data.Length)
            {
                int n = z.Read(buffer, 0, Math.Min(size, data.Length - total));
                Assert.True(n > 0); total += n;
            }
            Assert.Throws<IOException>(() => z.Read(buffer, 0, size));
        }

        [Fact]
        public async Task AsyncReadDrainsBufferedOutputBeforeRefilling()
        {
            using var z = new ZInputStream(new WithheldTrailer(CompletionTests.Pack(new byte[50000])));
            var one = new byte[1];
            for (int i = 0; i < 50000; i++) { Assert.Equal(1, await z.ReadAsync(one, 0, 1)); }
            await Assert.ThrowsAsync<IOException>(() => z.ReadAsync(one, 0, 1));
        }
    }
}
