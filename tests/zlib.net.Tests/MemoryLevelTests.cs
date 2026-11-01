using System;
using System.IO;
using ComponentAce.Compression.Libs.zlib;
using Xunit;

namespace zlib.net.Tests
{
    public class MemoryLevelTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(6)]
        [InlineData(9)]
        public void EveryMemoryLevelRoundtripsAndResets(int level)
        {
            foreach (int memory in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 })
            { foreach (byte[] data in new[] { TestData.Text(90000), TestData.Random(70000), TestData.Mixed(130000), Array.Empty<byte>() })
            {
                using var sink = new MemoryStream();
                using var z = new ZOutputStream(sink, level, true, 113, memory);
                for (int pass = 0; pass < 2; pass++)
                {
                    if (pass != 0) { z.Reset(); sink.SetLength(0); sink.Position = 0; }
                    for (int start = 0; start < data.Length; start += 1997)
                        { z.Write(data, start, Math.Min(1997, data.Length - start)); }
                    z.Finish();
                    using var decoder = new ZInputStream(new MemoryStream(sink.ToArray()));
                    using var actual = new MemoryStream(); decoder.CopyTo(actual);
                    Assert.Equal(data, actual.ToArray());
#if NET8_0_OR_GREATER
                    using var platform = new System.IO.Compression.ZLibStream(new MemoryStream(sink.ToArray()), System.IO.Compression.CompressionMode.Decompress);
                    actual.SetLength(0); actual.Position = 0; platform.CopyTo(actual);
                    Assert.Equal(data, actual.ToArray());
#endif
                }
            } }
        }

        [Fact]
        public void LowerMemoryLevelReducesBuffersAndPreservesWindow()
        {
            var z = new ZStream();
            try
            {
                Assert.Equal(0, z.deflateInit(6, 15, 6));
                Assert.Equal(65536, z.dstate.window.Length);
                Assert.Equal(32768, z.dstate.prev.Length);
                Assert.Equal(8192, z.dstate.head.Length);
                Assert.Equal(16384, z.dstate.pending_buf.Length);
            }
            finally { z.deflateEnd(); }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(10)]
        public void InvalidMemoryLevelsAreRejected(int memory)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ZOutputStream(Stream.Null, 6, true, 16384, memory));
            var z = new ZStream(); Assert.Equal(-2, z.deflateInit(6, 15, memory)); z.deflateEnd();
        }

        [Fact]
        public void ExplicitDefaultProducesIdenticalOutput()
        {
            byte[] data = TestData.Text(100000);
            using var original = new MemoryStream(); using var explicitDefault = new MemoryStream();
            using (var z = new ZOutputStream(original, 6, true)) z.Write(data, 0, data.Length);
            using (var z = new ZOutputStream(explicitDefault, 6, true, 16384, 8)) z.Write(data, 0, data.Length);
            Assert.Equal(original.ToArray(), explicitDefault.ToArray());
        }
    }
}
