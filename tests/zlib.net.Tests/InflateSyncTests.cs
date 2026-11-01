using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ComponentAce.Compression.Libs.zlib;
using Xunit;
namespace zlib.net.Tests
{
    public class InflateSyncTests
    {
        private static byte[] Raw(byte[] data)
        {
            var target = new MemoryStream();
            using (var z = new DeflateStream(target, CompressionLevel.Optimal, true)) z.Write(data, 0, data.Length);
            return target.ToArray();
        }

        [Fact]
        public void RecoverySearchIncludesAlreadyBufferedBytes()
        {
            var z = new ZStream(); z.inflateInit(-15);
            var payload = Encoding.ASCII.GetBytes("recovered payload");
            z.istate.blocks.bitb = 0x00ff0000;
            z.istate.blocks.bitk = 24; // first three bytes of 00 00 FF FF
            z.total_in = 3;
            z.next_in = new byte[] { 0xff }.Concat(Raw(payload)).ToArray();
            z.avail_in = z.next_in.Length;
            z.next_out = new byte[100]; z.avail_out = 100;
            try
            {
                Assert.Equal(0, z.inflateSync());
                Assert.Equal(4, z.total_in); // Buffered bytes must not be counted twice.
                Assert.Equal(1, z.inflate(0));
                Assert.Equal(payload, z.next_out.Take(z.next_out_index).ToArray());
            }
            finally { z.inflateEnd(); }
        }

        [Fact]
        public void WrappedRecoveryConsumesTrailerWithoutCheckingLostPrefix()
        {
            var data = Encoding.ASCII.GetBytes("valid suffix after lost data");
            var input = new byte[] { 0x78, 0x9c, 0x07, 0, 0, 0xff, 0xff }
                .Concat(Raw(data)).Concat(new byte[4]).ToArray();
            var z = new ZStream(); z.inflateInit();
            z.next_in = input; z.avail_in = input.Length;
            z.next_out = new byte[100]; z.avail_out = 100;
            try
            {
                Assert.Equal(-3, z.inflate(0));
                Assert.Equal(0, z.inflateSync());
                Assert.Equal(1, z.inflate(0));
                Assert.Equal(0, z.avail_in);
                Assert.Equal(data, z.next_out.Take(z.next_out_index).ToArray());
            }
            finally { z.inflateEnd(); }
        }

        [Fact]
        public void RecoveryBeforeHeaderAndAcrossCallsUsesRawBlocks()
        {
            var z = new ZStream(); z.inflateInit();
            z.next_in = new byte[] { 0, 0, 0xff }; z.avail_in = 3;
            try
            {
                Assert.Equal(-3, z.inflateSync());
                z.next_in = new byte[] { 0xff, 0x03, 0 }; z.next_in_index = 0; z.avail_in = 3;
                Assert.Equal(0, z.inflateSync());
                z.next_out = new byte[10]; z.avail_out = 10;
                Assert.Equal(1, z.inflate(0));
                Assert.Equal(0, z.total_out);
            }
            finally { z.inflateEnd(); }
        }
    }
}
