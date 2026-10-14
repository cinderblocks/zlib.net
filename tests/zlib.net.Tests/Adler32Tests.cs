using System;
using ComponentAce.Compression.Libs.zlib;
using Xunit;

namespace zlib.net.Tests
{
    public class Adler32Tests
    {
        private static long Reference(long adler, byte[] data, int index, int length)
        {
            ulong s1 = (ulong)(adler & 0xffff), s2 = (ulong)((adler >> 16) & 0xffff);
            for (int i = 0; i < length; i++)
            {
                s1 = (s1 + data[index + i]) % 65521;
                s2 = (s2 + s1) % 65521;
            }
            return (long)((s2 << 16) | s1);
        }

        [Fact]
        public void NullBufferReturnsInitialValue() => Assert.Equal(1L, Adler32.adler32(0, null, 0, 0));

        [Fact]
        public void KnownValue() =>
            // From the zlib/RFC 1950 definition: adler32("Wikipedia") == 0x11E60398
            Assert.Equal(0x11E60398L, Adler32.adler32(1, System.Text.Encoding.ASCII.GetBytes("Wikipedia"), 0, 9));

        [Theory]
        [InlineData(0), InlineData(1), InlineData(15), InlineData(16), InlineData(17), InlineData(31), InlineData(32), InlineData(33)]
        [InlineData(63), InlineData(64), InlineData(65), InlineData(5551), InlineData(5552), InlineData(5553)]
        [InlineData(11103), InlineData(11104), InlineData(11105), InlineData(100_000)]
        public void MatchesReferenceAtBoundaries(int length)
        {
            var data = TestData.Random(length + 7, 5);
            for (int i = 0; i < data.Length; i++) data[i] |= 0xF0; // large byte values stress overflow
            Assert.Equal(Reference(1, data, 0, length), Adler32.adler32(1, data, 0, length));
            Assert.Equal(Reference(1, data, 3, length), Adler32.adler32(1, data, 3, length));
        }

        [Fact]
        public void AllLengthsUpTo300AndMaxValues()
        {
            var data = new byte[400];
            for (int i = 0; i < data.Length; i++) data[i] = 255;
            for (int len = 0; len <= 300; len++)
                Assert.Equal(Reference(1, data, 1, len), Adler32.adler32(1, data, 1, len));
        }

        [Theory]
        [InlineData(5552), InlineData(5553), InlineData(11104), InlineData(100_000), InlineData(1_000_003)]
        public void WorstCaseAllMaxBytesDoesNotOverflow(int length)
        {
            var data = new byte[length + 5];
            for (int i = 0; i < data.Length; i++) data[i] = 255;
            // Start from the largest legal state as well as the initial one.
            Assert.Equal(Reference(1, data, 2, length), Adler32.adler32(1, data, 2, length));
            long max = (65520L << 16) | 65520L;
            Assert.Equal(Reference(max, data, 2, length), Adler32.adler32(max, data, 2, length));
        }

        [Fact]
        public void IsIncremental()
        {
            var data = TestData.Random(50_000, 9);
            long whole = Adler32.adler32(1, data, 0, data.Length);
            long a = 1;
            int pos = 0;
            foreach (int chunk in new[] { 1, 7, 5552, 5553, 100, 20_000 })
            {
                a = Adler32.adler32(a, data, pos, chunk);
                pos += chunk;
            }
            a = Adler32.adler32(a, data, pos, data.Length - pos);
            Assert.Equal(whole, a);
            Assert.Equal(Reference(1, data, 0, data.Length), whole);
        }
    }
}
