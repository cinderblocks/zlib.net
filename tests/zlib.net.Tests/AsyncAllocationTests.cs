#if NET8_0_OR_GREATER && !LIB_NETSTANDARD20
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ComponentAce.Compression.Libs.zlib;
using Xunit;
namespace zlib.net.Tests
{
    public class AsyncAllocationTests
    {
        [Fact]
        public async Task BufferedMemoryReadsCompleteWithoutPerReadTaskAllocations()
        {
            byte[] packed = CompletionTests.Pack(new byte[20000]);
            using var z = new ZInputStream(new MemoryStream(packed)); var b = new byte[16];
            // Warm the generic ValueTask machinery and leave ample decoded data buffered.
            for (int i = 0; i < 20; i++) Assert.Equal(16, (await z.ReadAsync(b.AsMemory())));
            long before = GC.GetAllocatedBytesForCurrentThread();
            int total = 0;
            for (int i = 0; i < 100; i++) total += (await z.ReadAsync(b.AsMemory()));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(1600, total);
            Assert.True(allocated < 1024, $"Unexpected per-read task allocation: {allocated}");
        }

        private sealed class DelayedSource : MemoryStream
        {
            public DelayedSource(byte[] data) : base(data) { }
            public override async ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken ct = default)
            {
                await Task.Yield(); ct.ThrowIfCancellationRequested();
                return await base.ReadAsync(b.Slice(0, Math.Min(b.Length, 7)), ct);
            }
        }
        [Fact]
        public async Task TrulyAsynchronousSourceStillDecodesCorrectly()
        {
            byte[] data = TestData.Mixed(100000);
            using var z = new ZInputStream(new DelayedSource(CompletionTests.Pack(data)));
            var output = new MemoryStream(); var b = new byte[4096]; int n;
            while ((n = await z.ReadAsync(b.AsMemory())) > 0) output.Write(b, 0, n);
            Assert.Equal(data, output.ToArray());
        }
    }
}
#endif
