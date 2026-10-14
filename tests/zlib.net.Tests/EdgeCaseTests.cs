using System;
using System.IO;
using System.IO.Compression;
using ComponentAce.Compression.Libs.zlib;
using Xunit;

namespace zlib.net.Tests
{
    public class EdgeCaseTests
    {
        private static byte[] Compress(byte[] input, int level, int flushMode = zlibConst.Z_NO_FLUSH, int chunk = 0)
        {
            var ms = new MemoryStream();
            using (var zout = new ZOutputStream(ms, level, leaveOpen: true) { FlushMode = flushMode })
            {
                if (chunk <= 0)
                    zout.Write(input, 0, input.Length);
                else
                    for (int off = 0; off < input.Length; off += chunk)
                        zout.Write(input, off, Math.Min(chunk, input.Length - off));
            }
            return ms.ToArray();
        }

        private static byte[] Decompress(byte[] input, int chunk = 0)
        {
            var ms = new MemoryStream();
            using (var zout = new ZOutputStream(ms, leaveOpen: true))
            {
                if (chunk <= 0)
                    zout.Write(input, 0, input.Length);
                else
                    for (int off = 0; off < input.Length; off += chunk)
                        zout.Write(input, off, Math.Min(chunk, input.Length - off));
            }
            return ms.ToArray();
        }

        private static byte[] DecompressWithZInputStream(byte[] input, int readSize)
        {
            using var zin = new ZInputStream(new MemoryStream(input));
            var result = new MemoryStream();
            var buf = new byte[readSize];
            int n;
            while ((n = zin.Read(buf, 0, buf.Length)) > 0)
                result.Write(buf, 0, n);
            return result.ToArray();
        }

        private static byte[] PlatformInflate(byte[] zlibData)
        {
            using var src = new MemoryStream(zlibData);
#if NET6_0_OR_GREATER
            using var z = new ZLibStream(src, CompressionMode.Decompress);
#else
            src.Position = 2;
            using var z = new DeflateStream(src, CompressionMode.Decompress);
#endif
            using var dst = new MemoryStream();
            z.CopyTo(dst);
            return dst.ToArray();
        }

#if NET6_0_OR_GREATER
        private static byte[] PlatformDeflate(byte[] data, CompressionLevel level)
        {
            var dst = new MemoryStream();
            using (var z = new ZLibStream(dst, level, leaveOpen: true))
                z.Write(data, 0, data.Length);
            return dst.ToArray();
        }
#endif

        // Overlapping copies (distance < length) must repeat the pattern rather than memmove it.
        [Theory]
        [InlineData(1), InlineData(2), InlineData(3), InlineData(4), InlineData(7)]
        public void OverlappingMatchesRoundTrip(int period)
        {
            var data = TestData.Periodic(300_000, period);
            foreach (int level in new[] { 1, 6, 9 })
            {
                var compressed = Compress(data, level);
                Assert.True(compressed.Length < 3_000);
                Assert.Equal(data, PlatformInflate(compressed));
                Assert.Equal(data, Decompress(compressed));
                Assert.Equal(data, Decompress(compressed, 1));
                Assert.Equal(data, DecompressWithZInputStream(compressed, 1000));
            }
        }

#if NET6_0_OR_GREATER
        [Theory]
        [InlineData(1), InlineData(2), InlineData(3), InlineData(5)]
        public void ReadsPlatformOverlappingMatches(int period)
        {
            var data = TestData.Periodic(300_000, period);
            foreach (var level in new[] { CompressionLevel.Fastest, CompressionLevel.Optimal, CompressionLevel.SmallestSize })
                Assert.Equal(data, Decompress(PlatformDeflate(data, level)));
        }

        [Fact]
        public void ReadsPlatformStoredBlocks()
        {
            var data = TestData.Random(200_000);
            var compressed = PlatformDeflate(data, CompressionLevel.NoCompression);
            Assert.Equal(data, Decompress(compressed));
            Assert.Equal(data, Decompress(compressed, 1));
            Assert.Equal(data, DecompressWithZInputStream(compressed, 4096));
        }

        [Fact]
        public void ReadsPlatformSmallFixedHuffmanBlocks()
        {
            for (int len = 0; len <= 400; len += 3)
            {
                var data = TestData.Text(len);
                foreach (var level in new[] { CompressionLevel.Fastest, CompressionLevel.Optimal })
                    Assert.Equal(data, Decompress(PlatformDeflate(data, level)));
            }
        }
#endif

        [Fact]
        public void StoredBlocksFromThisLibrary()
        {
            var data = TestData.Random(200_000);
            var compressed = Compress(data, zlibConst.Z_NO_COMPRESSION);
            Assert.True(compressed.Length > data.Length);
            Assert.Equal(data, PlatformInflate(compressed));
            Assert.Equal(data, Decompress(compressed));
            Assert.Equal(data, Decompress(compressed, 1));
        }

        [Fact]
        public void SmallInputsAtEveryLength()
        {
            for (int len = 0; len <= 300; len++)
            {
                var data = TestData.Text(len, (ulong)len);
                foreach (int level in new[] { 1, 6, 9 })
                {
                    var compressed = Compress(data, level);
                    Assert.Equal(data, PlatformInflate(compressed));
                    Assert.Equal(data, Decompress(compressed));
                }
            }
        }

        [Theory]
        [InlineData(zlibConst.Z_SYNC_FLUSH)]
        [InlineData(zlibConst.Z_FULL_FLUSH)]
        [InlineData(zlibConst.Z_PARTIAL_FLUSH)]
        public void FlushModesProduceValidStreams(int flushMode)
        {
            var data = TestData.Text(100_000);
            var compressed = Compress(data, 6, flushMode, chunk: 777);
            Assert.Equal(data, PlatformInflate(compressed));
            Assert.Equal(data, Decompress(compressed));
            Assert.Equal(data, Decompress(compressed, 13));
        }

        [Fact]
        public void SingleByteReadsFromZInputStream()
        {
            var data = TestData.Mixed(20_000);
            var compressed = Compress(data, 6);
            Assert.Equal(data, DecompressWithZInputStream(compressed, 1));
            Assert.Equal(data, DecompressWithZInputStream(compressed, 3));
        }
#if NET8_0_OR_GREATER && !LIB_NETSTANDARD20
        [Fact]
        public async System.Threading.Tasks.Task SpanAndMemoryWritesOfLargeInputsRoundTrip()
        {
            var data = TestData.Text(3_000_000);

            var viaSpan = new MemoryStream();
            using (var zout = new ZOutputStream(viaSpan, 6, leaveOpen: true))
                zout.Write(new ReadOnlySpan<byte>(data));
            Assert.Equal(data, PlatformInflate(viaSpan.ToArray()));

            var viaMemory = new MemoryStream();
            await using (var zout = new ZOutputStream(viaMemory, 6, leaveOpen: true))
                await zout.WriteAsync(new ReadOnlyMemory<byte>(data));
            Assert.Equal(data, PlatformInflate(viaMemory.ToArray()));

            // A non-array-backed memory forces the chunked path.
            var viaOwned = new MemoryStream();
            using (var owner = new NonArrayMemory(data))
            await using (var zout = new ZOutputStream(viaOwned, 6, leaveOpen: true))
                await zout.WriteAsync(owner.Memory);
            Assert.Equal(data, PlatformInflate(viaOwned.ToArray()));

            // All three paths must give the same bytes as the array overload.
            Assert.Equal(Compress(data, 6), viaSpan.ToArray());
            Assert.Equal(viaSpan.ToArray(), viaMemory.ToArray());
            Assert.Equal(viaSpan.ToArray(), viaOwned.ToArray());
        }

        private sealed class NonArrayMemory : System.Buffers.MemoryManager<byte>
        {
            private readonly byte[] data;
            public NonArrayMemory(byte[] data) { this.data = data; }
            public override Span<byte> GetSpan() => data;
            public override System.Buffers.MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
            public override void Unpin() { }
            protected override void Dispose(bool disposing) { }
            protected override bool TryGetArray(out ArraySegment<byte> segment) { segment = default; return false; }
        }
#endif

        [Fact]
        public void WriteByteThroughStreamReference()
        {
            var data = TestData.Text(5_000);
            var ms = new MemoryStream();
            using (var zout = new ZOutputStream(ms, 6, leaveOpen: true))
            {
                Stream s = zout;
                foreach (var b in data)
                    s.WriteByte(b);
            }
            Assert.Equal(data, PlatformInflate(ms.ToArray()));
        }

        // Seeded randomized differential test against the platform zlib.
        [Fact]
        public void RandomizedDifferential()
        {
            var rng = new TestData.Rng(12345);
            for (int iteration = 0; iteration < 60; iteration++)
            {
                int size = rng.Next(4) == 0 ? rng.Next(300) : rng.Next(200_000);
                var data = TestData.Shape(rng.Next(5), size, (ulong)iteration + 100);
                int level = rng.Next(11) - 1; // -1 (default) .. 9
                int chunk = rng.Next(3) == 0 ? 0 : 1 + rng.Next(10_000);

                var ours = Compress(data, level, zlibConst.Z_NO_FLUSH, chunk);
                Assert.Equal(data, PlatformInflate(ours));
                Assert.Equal(data, Decompress(ours, rng.Next(2) == 0 ? 0 : 1 + rng.Next(5000)));
#if NET6_0_OR_GREATER
                var theirs = PlatformDeflate(data, rng.Next(3) == 0 ? CompressionLevel.Fastest : CompressionLevel.Optimal);
                Assert.Equal(data, Decompress(theirs, rng.Next(2) == 0 ? 0 : 1 + rng.Next(5000)));
                Assert.Equal(data, DecompressWithZInputStream(theirs, 1 + rng.Next(5000)));
#endif
            }
        }
    }
}
