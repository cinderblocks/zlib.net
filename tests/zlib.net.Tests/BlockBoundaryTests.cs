using System;
using System.IO;
using System.Linq;
using ComponentAce.Compression.Libs.zlib;
using Xunit;

namespace zlib.net.Tests
{
    public class BlockBoundaryTests
    {
        [Fact]
        public void MixedDataAvoidsTheLegacyEarlySplitSizePenalty()
        {
            byte[] data = TestData.Mixed(1 << 20);
            using var sink = new MemoryStream();
            using (var z = new ZOutputStream(sink, 6, true)) { z.Write(data, 0, data.Length); }
            // The old unconditional TRUNCATE_BLOCK heuristic produced 363,985 bytes.
            // This bound permits different encodings while guarding the observed size improvement.
            Assert.True(sink.Length < 363500, $"Mixed-data size regression: {sink.Length}");
            AssertRoundtrip(data, sink.ToArray());
        }

        [Theory]
        [InlineData(1, 8)]
        [InlineData(6, 8)]
        [InlineData(9, 9)]
        public void EntropyTransitionsRemainValidWithSmallOutputBuffers(int level, int memoryLevel)
        {
            byte[] data = TestData.Random(70000).Concat(new byte[90000]).Concat(TestData.Text(140000)).Concat(TestData.Random(90000, 17)).ToArray();
            using var sink = new MemoryStream();
            using var z = new ZOutputStream(sink, level, true, 7, memoryLevel);
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass != 0) { z.Reset(); sink.SetLength(0); sink.Position = 0; }
                for (int offset = 0; offset < data.Length; offset += 8191)
                {
                    z.Write(data, offset, Math.Min(8191, data.Length - offset));
                }
                z.Finish(); AssertRoundtrip(data, sink.ToArray());
            }
        }

        private static void AssertRoundtrip(byte[] data, byte[] packed)
        {
            using var decoder = new ZInputStream(new MemoryStream(packed));
            using var actual = new MemoryStream(); decoder.CopyTo(actual); Assert.Equal(data, actual.ToArray());
#if NET8_0_OR_GREATER
            using var platform = new System.IO.Compression.ZLibStream(new MemoryStream(packed), System.IO.Compression.CompressionMode.Decompress);
            actual.SetLength(0); actual.Position = 0; platform.CopyTo(actual); Assert.Equal(data, actual.ToArray());
#endif
        }
    }
}
