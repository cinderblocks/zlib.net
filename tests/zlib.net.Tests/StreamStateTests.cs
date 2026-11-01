using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ComponentAce.Compression.Libs.zlib;
using Xunit;
namespace zlib.net.Tests
{
    public class StreamStateTests
    {
        [Fact]
        public void CanWriteReflectsCompletionEndAndFailure()
        {
            var z = new ZOutputStream(Stream.Null, 6, true); Assert.True(z.CanWrite);
            z.Finish(); Assert.False(z.CanWrite); z.End(); Assert.False(z.CanWrite); z.Dispose(); Assert.False(z.CanWrite);
            z = new ZOutputStream(new ThrowingSink(), 6, true); Assert.Throws<IOException>(() => z.Write(new byte[1], 0, 1));
            Assert.False(z.CanWrite); z.Dispose();
        }
        private sealed class ThrowingSink : MemoryStream
        {
            public override void Write(byte[] data, int offset, int count) => throw new IOException();
        }
        [Fact]
        public async Task EmptyAndCompletedArrayReadsHonorCancellation()
        {
            using var z = new ZInputStream(new MemoryStream(CompletionTests.Pack(Array.Empty<byte>())));
            var ct = new CancellationToken(true);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => z.ReadAsync(new byte[1], 0, 0, ct));
            Assert.Equal(0, z.Read(new byte[1], 0, 1));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => z.ReadAsync(new byte[1], 0, 1, ct));
#if NET8_0_OR_GREATER && !LIB_NETSTANDARD20
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await z.ReadAsync(Memory<byte>.Empty, ct));
#endif
        }
    }
}
