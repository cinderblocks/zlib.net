using System;
using System.IO;
using System.IO.Compression;
using ComponentAce.Compression.Libs.zlib;
using Xunit;

namespace zlib.net.Tests
{
    public class RoundTripTests
    {
        public static TheoryData<int> Levels => new TheoryData<int>
        {
            zlibConst.Z_NO_COMPRESSION,
            zlibConst.Z_BEST_SPEED,
            4,
            zlibConst.Z_DEFAULT_COMPRESSION,
            zlibConst.Z_BEST_COMPRESSION,
        };

        public static TheoryData<int> Sizes => new TheoryData<int>
        {
            0, 1, 511, 512, 513, 4095, 4096, 4097, 100_000, 1_000_000,
        };

        // Compressible but not trivially so.
        private static byte[] MakeData(int length, int seed = 42)
        {
            var rng = new Random(seed);
            var data = new byte[length];
            for (int i = 0; i < length; i++)
                data[i] = (byte)(rng.Next(4) == 0 ? rng.Next(256) : 'a' + (i % 13));
            return data;
        }

        private static byte[] Compress(byte[] input, int level, int chunk = 0)
        {
            var ms = new MemoryStream();
            using (var zout = new ZOutputStream(ms, level))
            {
                if (chunk <= 0)
                    zout.Write(input, 0, input.Length);
                else
                    for (int off = 0; off < input.Length; off += chunk)
                        zout.Write(input, off, Math.Min(chunk, input.Length - off));
                zout.finish();
                zout.Flush();
                // ZOutputStream disposes the underlying stream, so grab bytes first.
                return ms.ToArray();
            }
        }

        private static byte[] Decompress(byte[] input, int chunk = 0)
        {
            var ms = new MemoryStream();
            using (var zout = new ZOutputStream(ms))
            {
                if (chunk <= 0)
                    zout.Write(input, 0, input.Length);
                else
                    for (int off = 0; off < input.Length; off += chunk)
                        zout.Write(input, off, Math.Min(chunk, input.Length - off));
                zout.finish();
                return ms.ToArray();
            }
        }

        private static byte[] PlatformInflate(byte[] zlibData)
        {
            using var src = new MemoryStream(zlibData);
#if NET6_0_OR_GREATER
            using var z = new ZLibStream(src, CompressionMode.Decompress);
#else
            // .NET Framework has no ZLibStream: skip the 2-byte zlib header and inflate the raw deflate data.
            src.Position = 2;
            using var z = new DeflateStream(src, CompressionMode.Decompress);
#endif
            using var dst = new MemoryStream();
            z.CopyTo(dst);
            return dst.ToArray();
        }

#if NET6_0_OR_GREATER
        private static byte[] PlatformDeflate(byte[] data)
        {
            var dst = new MemoryStream();
            using (var z = new ZLibStream(dst, CompressionLevel.Optimal, leaveOpen: true))
                z.Write(data, 0, data.Length);
            return dst.ToArray();
        }
#endif

        [Theory]
        [MemberData(nameof(Sizes))]
        public void RoundTripsThroughItself(int size)
        {
            var data = MakeData(size);
            Assert.Equal(data, Decompress(Compress(data, zlibConst.Z_DEFAULT_COMPRESSION)));
        }

        [Theory]
        [MemberData(nameof(Levels))]
        public void OutputIsReadableByPlatformZlib(int level)
        {
            var data = MakeData(200_000);
            Assert.Equal(data, PlatformInflate(Compress(data, level)));
        }

#if NET6_0_OR_GREATER
        [Theory]
        [MemberData(nameof(Sizes))]
        public void ReadsOutputFromPlatformZlib(int size)
        {
            var data = MakeData(size);
            Assert.Equal(data, Decompress(PlatformDeflate(data)));
        }

#endif

        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(4097)]
        public void ChunkedWritesRoundTrip(int chunk)
        {
            var data = MakeData(50_000);
            var compressed = Compress(data, zlibConst.Z_DEFAULT_COMPRESSION, chunk);
            Assert.Equal(data, PlatformInflate(compressed));
            Assert.Equal(data, Decompress(compressed, chunk));
        }

        [Fact]
        public void WriteHonoursOffset()
        {
            var data = MakeData(10_000);
            var padded = new byte[data.Length + 300];
            Array.Copy(data, 0, padded, 100, data.Length);

            var ms = new MemoryStream();
            using (var zout = new ZOutputStream(ms, zlibConst.Z_DEFAULT_COMPRESSION))
            {
                zout.Write(padded, 100, data.Length);
                zout.finish();
                Assert.Equal(data, PlatformInflate(ms.ToArray()));
            }
        }

        [Fact]
        public void SingleByteWritesWork()
        {
            var data = MakeData(2_000);
            var ms = new MemoryStream();
            using (var zout = new ZOutputStream(ms, zlibConst.Z_DEFAULT_COMPRESSION))
            {
                foreach (var b in data)
                    zout.WriteByte(b);
                zout.finish();
                Assert.Equal(data, PlatformInflate(ms.ToArray()));
            }
        }

#if NET6_0_OR_GREATER
        [Fact]
        public void ZInputStreamDecompressesPlatformOutput()
        {
            var data = MakeData(30_000);
            using var zin = new ZInputStream(new MemoryStream(PlatformDeflate(data)));
            var result = new MemoryStream();
            var buf = new byte[1000];
            int n;
            while ((n = zin.read(buf, 0, buf.Length)) > 0)
                result.Write(buf, 0, n);
            Assert.Equal(data, result.ToArray());
        }

#endif

        [Fact]
        public void CorruptInputThrowsZStreamException()
        {
            var good = Compress(MakeData(5_000), zlibConst.Z_DEFAULT_COMPRESSION);
            good[good.Length / 2] ^= 0xFF;
            good[good.Length / 2 + 1] ^= 0xFF;
            Assert.Throws<ZStreamException>(() => Decompress(good));
        }

        [Fact]
        public void DisposeIsIdempotent()
        {
            var zout = new ZOutputStream(new MemoryStream(), zlibConst.Z_DEFAULT_COMPRESSION);
            zout.Dispose();
            zout.Dispose();
        }

#if NET8_0_OR_GREATER
        [Fact]
        public void SpanWriteRoundTrips()
        {
            var data = MakeData(70_000);
            var ms = new MemoryStream();
            using var zout = new ZOutputStream(ms, zlibConst.Z_DEFAULT_COMPRESSION);
            zout.Write(new ReadOnlySpan<byte>(data));
            zout.finish();
            Assert.Equal(data, PlatformInflate(ms.ToArray()));
        }

        [Fact]
        public async System.Threading.Tasks.Task AsyncWritesRoundTrip()
        {
            var data = MakeData(70_000);
            var ms = new MemoryStream();
            await using (var zout = new ZOutputStream(ms, zlibConst.Z_DEFAULT_COMPRESSION))
            {
                await zout.WriteAsync(data, 0, 30_000);
                await zout.WriteAsync(new ReadOnlyMemory<byte>(data, 30_000, data.Length - 30_000));
                await zout.FinishAsync();
                Assert.Equal(data, PlatformInflate(ms.ToArray()));
            }
        }
#endif
    }
}
