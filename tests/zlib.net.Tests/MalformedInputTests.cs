using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using ComponentAce.Compression.Libs.zlib;
using Xunit;

namespace zlib.net.Tests
{
    /// <summary>
    /// Untrusted input must never hang, run away in memory, or fail with anything other than <see cref="ZStreamException"/>.
    /// </summary>
    public class MalformedInputTests
    {
        private static readonly TimeSpan CaseTimeout = TimeSpan.FromSeconds(30);

        // Deflate cannot expand data by more than about 1032:1, so anything beyond this is a runaway.
        private static long OutputLimit(int inputLength) => 1100L * inputLength + 4096;

        private sealed class LimitedSink : Stream
        {
            private readonly long limit;
            public long Length_;
            public LimitedSink(long limit) { this.limit = limit; }
            public override void Write(byte[] buffer, int offset, int count)
            {
                Length_ += count;
                if (Length_ > limit)
                    throw new InvalidOperationException($"output exceeded {limit} bytes");
            }
            public override void Flush() { }
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }

        /// <summary>Feeds the data to ZOutputStream in chunks. Returns true if it completed, false on ZStreamException.</summary>
        private static bool ViaOutputStream(byte[] data, int chunk, Stream sink)
        {
            try
            {
                using var zout = new ZOutputStream(sink, leaveOpen: true);
                for (int off = 0; off < data.Length; off += chunk)
                    zout.Write(data, off, Math.Min(chunk, data.Length - off));
                zout.Finish();
                return true;
            }
            catch (ZStreamException)
            {
                return false;
            }
        }

        private static bool ViaInputStream(byte[] data, int readSize, Stream sink)
        {
            try
            {
                using var zin = new ZInputStream(new MemoryStream(data));
                var buf = new byte[readSize];
                int n;
                while ((n = zin.Read(buf, 0, buf.Length)) > 0)
                    sink.Write(buf, 0, n);
                return true;
            }
            catch (ZStreamException)
            {
                return false;
            }
        }

        /// <summary>Runs every entry point over the data. Only a clean result or ZStreamException is acceptable.</summary>
        private static void MustFailCleanlyOrSucceed(byte[] data, string what)
        {
            long limit = OutputLimit(data.Length);
            try
            {
                RunWithTimeout(() =>
                {
                    ViaOutputStream(data, Math.Max(1, data.Length), new LimitedSink(limit));
                    ViaOutputStream(data, 1, new LimitedSink(limit));
                    ViaOutputStream(data, 7, new LimitedSink(limit));
                    ViaInputStream(data, 4096, new LimitedSink(limit));
                    ViaInputStream(data, 1, new LimitedSink(limit));
                });
            }
            catch (Exception e) when (!(e is Xunit.Sdk.XunitException))
            {
                throw new Xunit.Sdk.XunitException($"{what}: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            }
        }

        private static void RunWithTimeout(Action action)
        {
            var task = Task.Run(action);
            if (!task.Wait(CaseTimeout))
                throw new Xunit.Sdk.XunitException("timed out (possible infinite loop)");
            task.GetAwaiter().GetResult();
        }

        private static byte[] Compress(byte[] input, int level)
        {
            var ms = new MemoryStream();
            using (var zout = new ZOutputStream(ms, level, leaveOpen: true))
                zout.Write(input, 0, input.Length);
            return ms.ToArray();
        }

        // Valid streams in the three block flavours: stored, fixed Huffman and dynamic Huffman.
        public static IEnumerable<object[]> Sources()
        {
            yield return new object[] { "stored", 0, TestData.Text(300, 11) };
            yield return new object[] { "fixed", 6, TestData.Text(40, 12) };
            yield return new object[] { "dynamic-text", 6, TestData.Text(1500, 13) };
            yield return new object[] { "dynamic-mixed", 9, TestData.Mixed(1200, 14) };
            yield return new object[] { "periodic", 1, TestData.Periodic(900, 3) };
        }

        [Theory]
        [MemberData(nameof(Sources))]
        public void EveryTruncationFailsCleanly(string name, int level, byte[] source)
        {
            var compressed = Compress(source, level);
            for (int len = 0; len < compressed.Length; len++)
            {
                var truncated = new byte[len];
                Array.Copy(compressed, truncated, len);
                MustFailCleanlyOrSucceed(truncated, $"{name}: truncated to {len} of {compressed.Length}");
            }
        }

        [Theory]
        [MemberData(nameof(Sources))]
        public void EverySingleBitFlipFailsCleanly(string name, int level, byte[] source)
        {
            var compressed = Compress(source, level);
            for (int bit = 0; bit < compressed.Length * 8; bit++)
            {
                var mutated = (byte[])compressed.Clone();
                mutated[bit >> 3] ^= (byte)(1 << (bit & 7));
                MustFailCleanlyOrSucceed(mutated, $"{name}: flipped bit {bit}");
            }
        }

        [Theory]
        [MemberData(nameof(Sources))]
        public void RandomMultiByteDamageFailsCleanly(string name, int level, byte[] source)
        {
            var compressed = Compress(source, level);
            var rng = new TestData.Rng(99);
            for (int i = 0; i < 400; i++)
            {
                var mutated = (byte[])compressed.Clone();
                int edits = 1 + rng.Next(6);
                for (int e = 0; e < edits; e++)
                    mutated[rng.Next(mutated.Length)] = (byte)rng.Next();
                MustFailCleanlyOrSucceed(mutated, $"{name}: iteration {i}");
            }
        }

        [Fact]
        public void RandomGarbageFailsCleanly()
        {
            var rng = new TestData.Rng(7);
            for (int i = 0; i < 1500; i++)
            {
                var garbage = TestData.Random(rng.Next(300), (ulong)(1000 + i));
                MustFailCleanlyOrSucceed(garbage, $"garbage #{i} ({garbage.Length} bytes)");

                // The same bytes behind a valid zlib header, so the garbage reaches the block decoder.
                var withHeader = new byte[garbage.Length + 2];
                withHeader[0] = 0x78; withHeader[1] = 0x9C;
                Array.Copy(garbage, 0, withHeader, 2, garbage.Length);
                MustFailCleanlyOrSucceed(withHeader, $"garbage-with-header #{i} ({garbage.Length} bytes)");
            }
        }

        [Fact]
        public void LargeGarbageFailsCleanly()
        {
            MustFailCleanlyOrSucceed(TestData.Random(200_000, 5), "200 KB of garbage");
            var withHeader = TestData.Random(200_000, 6);
            withHeader[0] = 0x78; withHeader[1] = 0x9C;
            MustFailCleanlyOrSucceed(withHeader, "200 KB of garbage behind a valid header");
        }

        [Theory]
        [MemberData(nameof(Sources))]
        public void WhenBothAcceptDamagedInputTheyAgree(string name, int level, byte[] source)
        {
            // The runtime's zlib is the oracle. Damage the deflate body, ask the runtime what it decodes to, then give the
            // stream a matching Adler-32 so that checksum failures don't hide the comparison. If we also accept the stream,
            // the output must be identical.
            var compressed = Compress(source, level);
            var rng = new TestData.Rng(31);
            int compared = 0;
            for (int i = 0; i < 600; i++)
            {
                var mutated = (byte[])compressed.Clone();
                int edits = 1 + rng.Next(3);
                for (int e = 0; e < edits; e++)
                    mutated[2 + rng.Next(mutated.Length - 6)] ^= (byte)(1 << rng.Next(8));

                byte[] expected;
                try
                {
                    using var raw = new MemoryStream(mutated, 2, mutated.Length - 6);
                    using var deflate = new DeflateStream(raw, CompressionMode.Decompress);
                    using var dst = new MemoryStream();
                    deflate.CopyTo(dst);
                    expected = dst.ToArray();
                }
                catch (InvalidDataException) { continue; }

                long adler = Adler32.adler32(1, expected, 0, expected.Length);
                mutated[mutated.Length - 4] = (byte)(adler >> 24);
                mutated[mutated.Length - 3] = (byte)(adler >> 16);
                mutated[mutated.Length - 2] = (byte)(adler >> 8);
                mutated[mutated.Length - 1] = (byte)adler;

                var ours = new MemoryStream();
                if (!ViaInputStream(mutated, 4096, ours))
                    continue; // e.g. the runtime stopped at an early final block and the "trailer" no longer lines up
                Assert.True(System.Linq.Enumerable.SequenceEqual(expected, ours.ToArray()), $"{name}: iteration {i}: outputs differ");
                compared++;
            }
            Assert.True(compared > 0, $"{name}: no damaged stream was decodable by both, so nothing was compared");
        }

        // --- Hand-built streams for the specific error paths ---

        private static byte[] Stream(string bits, params byte[] header)
        {
            var bytes = new List<byte>(header);
            for (int i = 0; i < bits.Length; i += 8)
            {
                int b = 0;
                for (int j = 0; j < 8 && i + j < bits.Length; j++)
                    if (bits[i + j] == '1') b |= 1 << j;
                bytes.Add((byte)b);
            }
            return bytes.ToArray();
        }

        // Writes an n-bit value least-significant bit first, as deflate does for non-Huffman fields.
        private static string Value(int v, int n)
        {
            var s = new char[n];
            for (int i = 0; i < n; i++) s[i] = (v >> i & 1) == 1 ? '1' : '0';
            return new string(s);
        }

        private static readonly byte[] ZlibHeader = { 0x78, 0x9C };

        private static void AssertRejected(byte[] data, string what)
        {
            RunWithTimeout(() =>
            {
                Assert.False(ViaOutputStream(data, data.Length, new LimitedSink(1 << 20)), $"{what}: ZOutputStream accepted it");
                Assert.False(ViaInputStream(data, 4096, new LimitedSink(1 << 20)), $"{what}: ZInputStream accepted it");
            });
        }

        [Fact]
        public void RejectsReservedBlockType()
            => AssertRejected(Stream("1" + Value(3, 2) + "00000", ZlibHeader), "block type 3");

        [Fact]
        public void RejectsStoredBlockWithMismatchedLengthComplement()
            => AssertRejected(Stream("1" + Value(0, 2) + "00000" + Value(5, 16) + Value(0, 16), ZlibHeader), "LEN/NLEN mismatch");

        [Fact]
        public void RejectsDistanceBeyondStartOfOutput()
        {
            // Fixed Huffman block: length code 257 (7 bits 0000001), distance code 0 (5 bits), before any output exists.
            var data = Stream("1" + Value(1, 2) + "0000001" + "00000" + "0000000", ZlibHeader);
            AssertRejected(data, "distance too far back");
        }

        [Fact]
        public void RejectsOversubscribedCodeLengthCodes()
        {
            // Dynamic block header: HLIT=0, HDIST=0, HCLEN=15, then nineteen 3-bit lengths all equal to 1.
            var bits = "1" + Value(2, 2) + Value(0, 5) + Value(0, 5) + Value(15, 4);
            for (int i = 0; i < 19; i++) bits += Value(1, 3);
            AssertRejected(Stream(bits, ZlibHeader), "oversubscribed code lengths");
        }

        [Fact]
        public void RejectsBadHeaderChecksum()
            => AssertRejected(new byte[] { 0x78, 0x9D, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01 }, "bad FCHECK");

        [Fact]
        public void RejectsUnknownCompressionMethod()
            => AssertRejected(new byte[] { 0x79, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01 }, "method 9");

        [Fact]
        public void RejectsCorruptChecksum()
        {
            var compressed = Compress(TestData.Text(2000), 6);
            compressed[compressed.Length - 1] ^= 0x01;
            AssertRejected(compressed, "corrupt Adler-32 trailer");
        }

        [Fact]
        public void PresetDictionaryRequestFailsCleanly()
        {
            // FDICT set (0x78BB) with no dictionary available. The shape of the error is up to the library, but it must be clean.
            MustFailCleanlyOrSucceed(new byte[] { 0x78, 0xBB, 0, 0, 0, 0, 0x03, 0x00 }, "FDICT");
        }

        [Fact]
        public void DecompressionBombStaysWithinTheExpansionLimit()
        {
            // 50 MB of zeros compresses to about 50 KB; it must decode correctly and in bounded memory.
            var compressed = Compress(new byte[50_000_000], 9);
            Assert.True(compressed.Length < 100_000);
            var sink = new LimitedSink(60_000_000);
            RunWithTimeout(() => Assert.True(ViaInputStream(compressed, 16 * 1024, sink)));
            Assert.Equal(50_000_000, sink.Length_);
        }
    }
}
