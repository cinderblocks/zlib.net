using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ComponentAce.Compression.Libs.zlib;
using Xunit;
namespace zlib.net.Tests
{
    public class RawInflateTests
    {
        internal static byte[] Inflate(byte[] packed, int inputChunk, int outputChunk)
        {
            var z = new ZStream();
            Assert.Equal(0, z.inflateInit(-15));
            z.next_in = packed;
            int supplied = 0;
            var target = new MemoryStream();
            try
            {
                for (int step = 0; step < 10000; step++)
                {
                    if (z.avail_in == 0 && supplied < packed.Length)
                    {
                        int n = Math.Min(inputChunk, packed.Length - supplied);
                        z.avail_in = n; supplied += n;
                    }
                    z.next_out = new byte[outputChunk]; z.next_out_index = 0; z.avail_out = outputChunk;
                    int status = z.inflate(0);
                    target.Write(z.next_out, 0, z.next_out_index);
                    if (status == zlibConst.Z_STREAM_END) return target.ToArray();
                    Assert.Equal(zlibConst.Z_OK, status);
                }
                throw new Exception("Inflater failed to terminate.");
            }
            finally { z.inflateEnd(); }
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(1, 4096)]
        [InlineData(100, 1)]
        [InlineData(100, 4096)]
        public void TerminalCodeNeedsNoDummyByte(int inputChunk, int outputChunk)
        {
            byte[] packed = { 0xcb, 0x48, 0xcd, 0xc9, 0xc9, 0x57, 0xc8, 0x40, 0x90, 0x00 };
            Assert.Equal(Encoding.ASCII.GetBytes("hello hello hello"), Inflate(packed, inputChunk, outputChunk));
        }

        [Fact]
        public void RawStreamsFromPlatformFinishAtEverySmallPayloadLength()
        {
            for (int length = 0; length <= 256; length++)
            {
                foreach (byte[] data in new[] { TestData.Text(length), TestData.Random(length), TestData.Periodic(length, 3) })
                {
                    var packed = new MemoryStream();
                    using (var z = new DeflateStream(packed, CompressionLevel.Optimal, true))
                        z.Write(data, 0, data.Length);
                    // .NET Framework emits no raw stream for an empty source.
                    if (packed.Length == 0) continue;
                    Assert.Equal(data, Inflate(packed.ToArray(), 1, 7));
                    Assert.Equal(data, Inflate(packed.ToArray(), int.MaxValue, 8192));
                }
            }
        }

        [Fact]
        public void IncompleteTerminalCodeIsNotAcceptedAsEndOfStream()
        {
            var z = new ZStream();
            z.inflateInit(-15);
            z.next_in = new byte[] { 0xcb, 0x48, 0xcd, 0xc9, 0xc9, 0x57, 0xc8, 0x40, 0x90 };
            z.avail_in = z.next_in.Length;
            z.next_out = new byte[100]; z.avail_out = 100;
            try
            {
                Assert.NotEqual(zlibConst.Z_STREAM_END, z.inflate(0));
                Assert.Equal(zlibConst.Z_BUF_ERROR, z.inflate(0));
            }
            finally { z.inflateEnd(); }
        }
    }
}
