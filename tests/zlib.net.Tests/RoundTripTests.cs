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
                zout.Finish();
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
                zout.Finish();
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
                zout.Finish();
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
                zout.Finish();
                Assert.Equal(data, PlatformInflate(ms.ToArray()));
            }
        }

        [Theory]
        [MemberData(nameof(Sizes))]
        public void ZInputStreamInflates(int size)
        {
            var data = MakeData(size);
            var compressed = Compress(data, zlibConst.Z_DEFAULT_COMPRESSION);
            using var zin = new ZInputStream(new MemoryStream(compressed));
            using var result = new MemoryStream();
            zin.CopyTo(result);
            Assert.Equal(data, result.ToArray());
        }

        [Theory]
        [MemberData(nameof(Sizes))]
        public void ZInputStreamDeflates(int size)
        {
            var data = MakeData(size);
            using var zin = new ZInputStream(new MemoryStream(data), zlibConst.Z_DEFAULT_COMPRESSION);
            using var compressed = new MemoryStream();
            zin.CopyTo(compressed);
            Assert.Equal(data, PlatformInflate(compressed.ToArray()));
        }

        [Fact]
        public void ZInputStreamReturnsZeroAfterEnd()
        {
            var compressed = Compress(MakeData(100), zlibConst.Z_DEFAULT_COMPRESSION);
            using var zin = new ZInputStream(new MemoryStream(compressed));
            var buf = new byte[1000];
            Assert.Equal(100, zin.Read(buf, 0, buf.Length));
            Assert.Equal(0, zin.Read(buf, 0, buf.Length));
            Assert.Equal(0, zin.Read(buf, 0, buf.Length));
            Assert.Equal(-1, zin.ReadByte());
        }

        [Fact]
        public void ZInputStreamReadByte()
        {
            var data = MakeData(1_000);
            using var zin = new ZInputStream(new MemoryStream(Compress(data, zlibConst.Z_DEFAULT_COMPRESSION)));
            foreach (var expected in data)
                Assert.Equal(expected, zin.ReadByte());
            Assert.Equal(-1, zin.ReadByte());
        }

        [Fact]
        public async System.Threading.Tasks.Task ZInputStreamReadAsync()
        {
            var data = MakeData(100_000);
            using var zin = new ZInputStream(new MemoryStream(Compress(data, zlibConst.Z_DEFAULT_COMPRESSION)));
            using var result = new MemoryStream();
            await zin.CopyToAsync(result);
            Assert.Equal(data, result.ToArray());
        }

        [Fact]
        public void ZInputStreamThrowsOnTruncatedInput()
        {
            var compressed = Compress(MakeData(20_000), zlibConst.Z_DEFAULT_COMPRESSION);
            var truncated = new byte[compressed.Length / 2];
            Array.Copy(compressed, truncated, truncated.Length);
            using var zin = new ZInputStream(new MemoryStream(truncated));
            Assert.Throws<ZStreamException>(() => zin.CopyTo(Stream.Null));
        }

        [Fact]
        public void ZInputStreamThrowsOnCorruptInput()
        {
            var compressed = Compress(MakeData(5_000), zlibConst.Z_DEFAULT_COMPRESSION);
            compressed[compressed.Length / 2] ^= 0xFF;
            compressed[compressed.Length / 2 + 1] ^= 0xFF;
            using var zin = new ZInputStream(new MemoryStream(compressed));
            Assert.Throws<ZStreamException>(() => zin.CopyTo(Stream.Null));
        }

        [Fact]
        public void LegacyLowercaseMembersStillWork()
        {
#pragma warning disable CS0618
            var data = MakeData(5_000);
            var ms = new MemoryStream();
            var zout = new ZOutputStream(ms, zlibConst.Z_DEFAULT_COMPRESSION, leaveOpen: true);
            zout.Write(data, 0, data.Length);
            zout.finish();
            zout.end();
            zout.Dispose();

            using var zin = new ZInputStream(new MemoryStream(ms.ToArray()));
            var buf = new byte[data.Length];
            int total = 0, n;
            while ((n = zin.read(buf, total, buf.Length - total)) > 0)
                total += n;
            Assert.Equal(data.Length, total);
            Assert.Equal(-1, zin.read(new byte[10], 0, 10));
            Assert.Equal(data, buf);
#pragma warning restore CS0618
        }

        [Fact]
        public void LeaveOpenKeepsUnderlyingStreamOpen()
        {
            var ms = new MemoryStream();
            using (var zout = new ZOutputStream(ms, zlibConst.Z_DEFAULT_COMPRESSION, leaveOpen: true))
                zout.Write(new byte[] { 1, 2, 3 }, 0, 3);
            Assert.True(ms.CanRead);
            Assert.Equal(new byte[] { 1, 2, 3 }, PlatformInflate(ms.ToArray()));

            var src = new MemoryStream(ms.ToArray());
            using (var zin = new ZInputStream(src, leaveOpen: true))
                zin.CopyTo(Stream.Null);
            Assert.True(src.CanRead);
        }

        [Fact]
        public void DisposeClosesUnderlyingStreamByDefault()
        {
            var ms = new MemoryStream();
            new ZOutputStream(ms, zlibConst.Z_DEFAULT_COMPRESSION).Dispose();
            Assert.False(ms.CanRead);
        }

        [Fact]
        public void UnsupportedMembersThrowNotSupported()
        {
            using var zout = new ZOutputStream(new MemoryStream(), zlibConst.Z_DEFAULT_COMPRESSION);
            Assert.False(zout.CanRead);
            Assert.False(zout.CanSeek);
            Assert.True(zout.CanWrite);
            Assert.Throws<NotSupportedException>(() => zout.Read(new byte[1], 0, 1));
            Assert.Throws<NotSupportedException>(() => zout.Seek(0, SeekOrigin.Begin));
            Assert.Throws<NotSupportedException>(() => zout.SetLength(0));
            Assert.Throws<NotSupportedException>(() => zout.Length);
            Assert.Throws<NotSupportedException>(() => zout.Position);
            Assert.Throws<NotSupportedException>(() => zout.Position = 0);

            using var zin = new ZInputStream(new MemoryStream());
            Assert.True(zin.CanRead);
            Assert.False(zin.CanSeek);
            Assert.False(zin.CanWrite);
            Assert.Throws<NotSupportedException>(() => zin.Write(new byte[1], 0, 1));
            Assert.Throws<NotSupportedException>(() => zin.Seek(0, SeekOrigin.Begin));
            Assert.Throws<NotSupportedException>(() => zin.Length);
        }

        [Fact]
        public void UsingDisposedStreamsThrowsObjectDisposed()
        {
            var zout = new ZOutputStream(new MemoryStream(), zlibConst.Z_DEFAULT_COMPRESSION);
            zout.Dispose();
            Assert.Throws<ObjectDisposedException>(() => zout.Write(new byte[1], 0, 1));

            var zin = new ZInputStream(new MemoryStream());
            zin.Dispose();
            zin.Dispose();
            Assert.Throws<ObjectDisposedException>(() => zin.Read(new byte[1], 0, 1));
        }

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

// The Span and async overloads exist only in the netstandard2.1 build.
#if NET8_0_OR_GREATER && !LIB_NETSTANDARD20
        [Fact]
        public void SpanWriteRoundTrips()
        {
            var data = MakeData(70_000);
            var ms = new MemoryStream();
            using var zout = new ZOutputStream(ms, zlibConst.Z_DEFAULT_COMPRESSION);
            zout.Write(new ReadOnlySpan<byte>(data));
            zout.Finish();
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
