using System;
using System.IO;
using System.Linq;
using ComponentAce.Compression.Libs.zlib;
using Xunit;
namespace zlib.net.Tests
{
    public class HistoryTests
    {
        [Theory]
        [InlineData(1, false)]
        [InlineData(1, true)]
        [InlineData(4096, false)]
        [InlineData(4096, true)]
        public void MissingHistoryIsRejectedInSlowAndFastPaths(int outputSize, bool padding)
        {
            // Final fixed block: length 3 / distance 1 before any output exists.
            byte[] raw = new byte[] { 0x03, 0x02, 0x00 };
            if (padding) raw = raw.Concat(new byte[20]).ToArray();
            var z = new ZStream();
            Assert.Equal(0, z.inflateInit(-15));
            z.next_in = raw; z.avail_in = raw.Length;
            z.next_out = new byte[outputSize]; z.avail_out = outputSize;
            try
            {
                Assert.Equal(zlibConst.Z_DATA_ERROR, z.inflate(0));
                Assert.Contains("distance too far back", z.msg);
                Assert.Equal(0, z.total_out);
            }
            finally { z.inflateEnd(); }
        }

        [Fact]
        public void StreamWrappersRejectMalformedHistoryEvenWithMatchingChecksum()
        {
            byte[] data = { 0x78, 0x9c, 0x03, 0x02, 0x00, 0x00, 0x03, 0x00, 0x01 };
            using (var zin = new ZInputStream(new MemoryStream(data)))
                Assert.Throws<ZStreamException>(() => zin.CopyTo(Stream.Null));
            using (var zout = new ZOutputStream(Stream.Null, true))
                Assert.Throws<ZStreamException>(() => zout.Write(data, 0, data.Length));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(81920)]
        public void HistorySurvivesStoredBlocksWindowWrapAndPartialReads(int readSize)
        {
            var data = TestData.Random(100000).Concat(TestData.Periodic(100000, 7)).ToArray();
            var compressed = CompletionTests.Pack(data);
            using var z = new ZInputStream(new MemoryStream(compressed));
            var target = new MemoryStream();
            var buffer = new byte[readSize];
            int n;
            while ((n = z.Read(buffer, 0, buffer.Length)) != 0) target.Write(buffer, 0, n);
            Assert.Equal(data, target.ToArray());
        }

        [Theory]
        [InlineData(32)]
        [InlineData(40000)]
        public void PresetDictionaryCountsAsInitializedHistory(int dictionaryLength)
        {
            byte[] dict = TestData.Random(dictionaryLength);
            byte[] data = dict.Skip(Math.Max(0, dict.Length - 1000)).Concat(new byte[] { 1, 2, 3 }).ToArray();
            var compressor = new ZStream();
            Assert.Equal(0, compressor.deflateInit(9));
            Assert.Equal(0, compressor.deflateSetDictionary(dict, dict.Length));
            var packed = new byte[data.Length * 2 + 100];
            compressor.next_in = data; compressor.avail_in = data.Length;
            compressor.next_out = packed; compressor.avail_out = packed.Length;
            Assert.Equal(1, compressor.deflate(zlibConst.Z_FINISH));
            int length = compressor.next_out_index;
            compressor.deflateEnd();
            var z = new ZStream();
            Assert.Equal(0, z.inflateInit());
            z.next_in = packed; z.avail_in = length;
            z.next_out = new byte[data.Length + 1]; z.avail_out = data.Length + 1;
            try
            {
                Assert.Equal(zlibConst.Z_NEED_DICT, z.inflate(0));
                Assert.Equal(0, z.inflateSetDictionary(dict, dict.Length));
                Assert.Equal(zlibConst.Z_STREAM_END, z.inflate(0));
                Assert.Equal(data, z.next_out.Take(z.next_out_index).ToArray());
            }
            finally { z.inflateEnd(); }
        }
    }
}
