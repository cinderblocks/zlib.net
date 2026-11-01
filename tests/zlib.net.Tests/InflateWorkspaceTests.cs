using System;
using System.IO;
using ComponentAce.Compression.Libs.zlib;
using Xunit;
namespace zlib.net.Tests
{
    public class InflateWorkspaceTests
    {
        internal static byte[] EmptyBlocks(int count)
        {
            var bytes = new System.Collections.Generic.List<byte> { 0x78, 0x9c }; int accumulator = 0, bits = 0;
            void Put(int value, int length) { accumulator |= value << bits; bits += length; while (bits >= 8) { bytes.Add((byte)accumulator); accumulator >>= 8; bits -= 8; } }
            for (int i = 0; i < count; i++) { Put(i == count - 1 ? 3 : 2, 3); Put(0, 7); }
            if (bits != 0) bytes.Add((byte)accumulator); bytes.AddRange(new byte[] { 0, 0, 0, 1 }); return bytes.ToArray();
        }

#if NET8_0_OR_GREATER
        [Fact]
        public void FixedBlockAllocationsDoNotGrowWithBlockCount()
        {
            long Measure(byte[] data)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                using (var z = new ZInputStream(new MemoryStream(data))) z.CopyTo(Stream.Null);
                return GC.GetAllocatedBytesForCurrentThread() - before;
            }
            byte[] one = EmptyBlocks(1), many = EmptyBlocks(10000);
            Measure(one); Measure(many); // warm pools and metadata outside the measurement
            long initial = Measure(one), repeated = Measure(many);
            Assert.True(repeated <= initial + 1024, $"Per-block allocations: {initial} -> {repeated}");
        }
#endif

        [Fact]
        public void DynamicScratchIsIndependentAcrossConcurrentStreams()
        {
            byte[] data = TestData.Text(50000), packed = CompletionTests.Pack(data);
            System.Threading.Tasks.Parallel.For(0, 16, _ =>
            {
                using var z = new ZInputStream(new MemoryStream(packed));
                var output = new MemoryStream(); z.CopyTo(output); Assert.Equal(data, output.ToArray());
            });
        }

        [Theory]
        [InlineData(0)]
        [InlineData(6)]
        public void StoredAndFixedBlocksDoNotAllocateDynamicTables(int level)
        {
            var packed = new MemoryStream(); using (var output = new ZOutputStream(packed, level, true)) output.Write(new byte[] { 1, 2, 3 }, 0, 3);
            var z = new ZStream(); z.inflateInit(); z.next_in = packed.ToArray(); z.avail_in = z.next_in.Length;
            z.next_out = new byte[10]; z.avail_out = 10;
            try { Assert.Equal(1, z.inflate(0)); Assert.Null(z.istate.blocks.hufts); }
            finally { z.inflateEnd(); }
        }
    }
}
