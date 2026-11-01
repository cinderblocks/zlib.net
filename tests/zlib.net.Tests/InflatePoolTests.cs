using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ComponentAce.Compression.Libs.zlib;
using Xunit;

namespace zlib.net.Tests
{
    public class InflatePoolTests
    {
        [Fact]
        public void LeasesAreExclusiveAndBeginCleared()
        {
            byte[] first = InflateBuffers.RentWindow(32768), second = InflateBuffers.RentWindow(32768);
            int[] table = InflateBuffers.RentTable(), otherTable = InflateBuffers.RentTable();
            try
            {
                Assert.NotSame(first, second); Assert.NotSame(table, otherTable);
                Assert.True(first.All(v => v == 0)); Assert.True(second.All(v => v == 0));
                Assert.True(table.All(v => v == 0)); Assert.True(otherTable.All(v => v == 0));
                first[0] = second[32767] = 123; table[0] = otherTable[4319] = 123;
            }
            finally
            {
                InflateBuffers.ReturnWindow(first); InflateBuffers.ReturnWindow(second);
                InflateBuffers.ReturnTable(table); InflateBuffers.ReturnTable(otherTable);
            }
        }

        [Fact]
        public void ConcurrentMixedDecodersRemainIndependentAfterErrorsAndDoubleEnd()
        {
            byte[][] samples = { TestData.Text(90000), TestData.Random(70000), new byte[100000], new byte[] { 1 } };
            byte[][] packed = samples.Select(CompletionTests.Pack).ToArray();
            Parallel.For(0, 32, worker =>
            {
                for (int pass = 0; pass < 4; pass++)
                {
                    int index = (worker + pass) % samples.Length;
                    var z = new ZStream(); Assert.Equal(0, z.inflateInit());
                    try
                    {
                        z.next_in = packed[index]; z.avail_in = packed[index].Length;
                        z.next_out = new byte[samples[index].Length + 1]; z.avail_out = z.next_out.Length;
                        Assert.Equal(1, z.inflate(0));
                        Assert.Equal(samples[index], z.next_out.Take((int)z.total_out).ToArray());
                    }
                    finally { Assert.Equal(0, z.inflateEnd()); Assert.Equal(-2, z.inflateEnd()); }
                    using var broken = new ZOutputStream(Stream.Null, true);
                    Assert.Throws<ZStreamException>(() => broken.Write(new byte[] { 0xff, 0xff }, 0, 2));
                }
            });
        }

        [Theory]
        [InlineData(8)]
        [InlineData(9)]
        [InlineData(12)]
        [InlineData(15)]
        public void WindowSizesArePreserved(int bits)
        {
            var z = new ZStream(); Assert.Equal(0, z.inflateInit(bits));
            try { Assert.Equal(1 << bits, z.istate.blocks.window.Length); }
            finally { z.inflateEnd(); }
        }
    }
}
