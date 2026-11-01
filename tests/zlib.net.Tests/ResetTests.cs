using System;
using System.IO;
using System.Linq;
using ComponentAce.Compression.Libs.zlib;
using Xunit;
namespace zlib.net.Tests
{
    public class ResetTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(6)]
        [InlineData(9)]
        public void ReusedCompressorProducesTheSameIndependentStreamsAsFreshInstances(int level)
        {
            var sink = new MemoryStream(); using var z = new ZOutputStream(sink, level, true);
            var samples = new[] { TestData.Text(100000), Array.Empty<byte>(), TestData.Random(2000), new byte[] { 42 }, TestData.Periodic(50000, 7) };
            for (int pass = 0; pass < 3; pass++) foreach (byte[] data in samples)
            {
                sink.SetLength(0); sink.Position = 0;
                z.Write(data, 0, data.Length); z.Finish();
                var fresh = new MemoryStream(); using (var other = new ZOutputStream(fresh, level, true)) other.Write(data, 0, data.Length);
                Assert.Equal(fresh.ToArray(), sink.ToArray());
                Assert.Equal(data.Length, z.TotalIn);
                z.Reset(); Assert.Equal(0, z.TotalIn); Assert.Equal(0, z.TotalOut); Assert.True(z.CanWrite);
            }
            z.Finish();
        }

        [Fact]
        public void ReusedDecoderClearsHistoryAndRestoresChecksumValidation()
        {
            var sink = new MemoryStream(); using var z = new ZOutputStream(sink, true);
            byte[] first = CompletionTests.Pack(TestData.Text(10000)); z.Write(first, 0, first.Length); z.Finish(); z.Reset();
            byte[] invalid = { 0x78, 0x9c, 0x03, 0x02, 0, 0, 3, 0, 1 };
            Assert.Throws<ZStreamException>(() => z.Write(invalid, 0, invalid.Length));
        }

        [Fact]
        public void ResetRejectsUnfinishedFailedAndEndedStreams()
        {
            var z = new ZOutputStream(Stream.Null, 6, true);
            Assert.Throws<InvalidOperationException>(() => z.Reset()); z.End();
            Assert.Throws<ObjectDisposedException>(() => z.Reset()); z.Dispose();
            Assert.Equal(zlibConst.Z_STREAM_ERROR, new ZStream().deflateReset());
            Assert.Equal(zlibConst.Z_STREAM_ERROR, new ZStream().inflateReset());
        }

        [Fact]
        public void LowLevelResetDropsDictionaryAndRecoveryState()
        {
            var z = new ZStream(); Assert.Equal(0, z.deflateInit(6));
            Assert.Equal(0, z.deflateSetDictionary(new byte[] { 1, 2, 3 }, 3));
            Assert.Equal(0, z.deflateReset());
            var target = new byte[100]; z.next_in = Array.Empty<byte>(); z.next_out = target; z.avail_out = target.Length;
            Assert.Equal(1, z.deflate(zlibConst.Z_FINISH)); Assert.Equal(0, target[1] & 0x20); z.deflateEnd();

            z = new ZStream(); z.inflateInit(); z.next_in = new byte[] { 0, 0, 255, 255 }; z.avail_in = 4;
            Assert.Equal(0, z.inflateSync()); // Recovery before a header temporarily switches to raw blocks.
            Assert.Equal(0, z.inflateReset());
            var data = CompletionTests.Pack(new byte[] { 1, 2, 3 }); data[data.Length - 1] ^= 1;
            z.next_in = data; z.next_in_index = 0; z.avail_in = data.Length; z.next_out = new byte[10]; z.avail_out = 10;
            Assert.Equal(zlibConst.Z_DATA_ERROR, z.inflate(0)); z.inflateEnd();
        }
    }
}
