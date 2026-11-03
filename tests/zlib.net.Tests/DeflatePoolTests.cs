using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ComponentAce.Compression.Libs.zlib;
using Xunit;
namespace zlib.net.Tests
{
    public class DeflatePoolTests
    {
        [Fact]
        public void ReturnedBuffersAreClearedBeforeTheyCanBeLeased()
        {
            var buffers = DeflateBuffers.Rent();
            try
            {
                Assert.True(buffers.Window.All(v => v == 0)); Assert.True(buffers.Pending.All(v => v == 0));
                Assert.True(buffers.Previous.All(v => v == 0)); Assert.True(buffers.Heads.All(v => v == 0));
                buffers.Window[0] = buffers.Pending[65535] = 42; buffers.Previous[123] = buffers.Heads[321] = 123;
            }
            finally { DeflateBuffers.Return(buffers); }
        }

        [Fact]
        public void ConcurrentStreamsRemainIndependentUnderCachePressure()
        {
            Parallel.For(0, 24, seed =>
            {
                byte[] data = TestData.Random(10000 + seed * 31);
                for (int i = 0; i < 4; i++)
                {
                    byte[] packed = CompletionTests.Pack(data);
                    using var z = new ZInputStream(new MemoryStream(packed));
                    var result = new MemoryStream(); z.CopyTo(result); Assert.Equal(data, result.ToArray());
                }
            });
        }

        [Theory]
        [InlineData(9)]
        [InlineData(12)]
        [InlineData(15)]
        public void CustomWindowSizesAndDefaultPoolDoNotMix(int bits)
        {
            var z = new ZStream(); Assert.Equal(0, z.deflateInit(6, bits));
            byte[] data = TestData.Text(5000); z.next_in = data; z.avail_in = data.Length;
            z.next_out = new byte[10000]; z.avail_out = z.next_out.Length;
            Assert.Equal(1, z.deflate(zlibConst.Z_FINISH)); var packed = z.next_out.Take(z.next_out_index).ToArray(); z.deflateEnd();
            using var decoder = new ZInputStream(new MemoryStream(packed));
            var output = new MemoryStream(); decoder.CopyTo(output); Assert.Equal(data, output.ToArray());
        }
    }
}
