using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ComponentAce.Compression.Libs.zlib;
using Xunit;
namespace zlib.net.Tests
{
    public class ValidationTests
    {
        [Theory]
        [InlineData(-2)]
        [InlineData(10)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        public void ConstructorsRejectInvalidLevelsWithoutClosingTheirStreams(int level)
        {
            var target = new MemoryStream();
            Assert.Throws<ArgumentOutOfRangeException>(() => new ZOutputStream(target, level));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ZInputStream(target, level));
            Assert.True(target.CanWrite);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(9)]
        public void ConstructorsAcceptValidLevels(int level)
        {
            using var output = new ZOutputStream(new MemoryStream(), level);
            using var input = new ZInputStream(new MemoryStream(), level);
        }

        [Fact]
        public void InvalidWriteArgumentsDoNotChangeTheCodecOrOutput()
        {
            var target = new MemoryStream();
            using var z = new ZOutputStream(target, 6, true);
            Assert.Throws<ArgumentNullException>(() => z.Write(null, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => z.Write(new byte[1], -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => z.Write(new byte[1], 2, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => z.Write(new byte[1], 0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => z.Write(new byte[1], 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => z.Write(new byte[1], int.MaxValue, int.MaxValue));
            Assert.Equal(0, z.TotalIn);
            Assert.Equal(0, target.Length);
            z.Write(new byte[] { 7 }, 0, 1);
            z.Finish();
            Assert.Equal(CompletionTests.Pack(new byte[] { 7 }), target.ToArray());
        }

#if NET8_0_OR_GREATER && !LIB_NETSTANDARD20
        [Fact]
        public async Task InvalidAsyncArgumentsAndCancellationDoNotChangeState()
        {
            var target = new MemoryStream();
            var z = new ZOutputStream(target, 6, true);
            await Assert.ThrowsAsync<ArgumentNullException>(() => z.WriteAsync(null, 0, 0));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => z.WriteAsync(new byte[1], 2, 0));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => z.WriteAsync(new byte[1], 0, 0, new CancellationToken(true)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await z.WriteAsync(ReadOnlyMemory<byte>.Empty, new CancellationToken(true)));
            Assert.Equal(0, z.TotalIn);
            await z.WriteAsync(new byte[] { 7 }.AsMemory());
            await z.DisposeAsync();
            Assert.Equal(CompletionTests.Pack(new byte[] { 7 }), target.ToArray());
        }
#endif
    }
}
