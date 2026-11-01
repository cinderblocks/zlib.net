#if NET8_0_OR_GREATER && !LIB_NETSTANDARD20
using System;
using System.IO;
using ComponentAce.Compression.Libs.zlib;
using Xunit;

namespace zlib.net.Tests
{
    public class SpanCompressionTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 0)]
        [InlineData(6, 0)]
        [InlineData(9, 0)]
        [InlineData(6, 1)]
        [InlineData(6, 2)]
        [InlineData(6, 3)]
        public void SlicedSpanMatchesArrayWritesAcrossFlushModes(int level, int flush)
        {
            byte[] source = TestData.Mixed(200013);
            using var arraySink = new MemoryStream(); using var spanSink = new MemoryStream();
            using (var array = new ZOutputStream(arraySink, level, true, 17))
            using (var span = new ZOutputStream(spanSink, level, true, 17))
            {
                array.FlushMode = span.FlushMode = flush;
                for (int start = 7; start < source.Length - 6; start += 20003)
                {
                    int count = Math.Min(20003, source.Length - 6 - start);
                    array.Write(source, start, count); span.Write(source.AsSpan(start, count));
                }
            }
            Assert.Equal(arraySink.ToArray(), spanSink.ToArray());
        }

        [Fact]
        public void StackBackedInputCanBeOverwrittenAfterWriteAndReset()
        {
            Span<byte> data = stackalloc byte[257];
            byte[] expected = TestData.Random(data.Length); expected.AsSpan().CopyTo(data);
            using var sink = new MemoryStream(); using var z = new ZOutputStream(sink, 6, true, 1);
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass != 0) { z.Reset(); sink.SetLength(0); sink.Position = 0; expected.AsSpan().CopyTo(data); }
                z.Write((ReadOnlySpan<byte>)data); data.Clear(); z.Finish();
                using var decoder = new System.IO.Compression.ZLibStream(new MemoryStream(sink.ToArray()), System.IO.Compression.CompressionMode.Decompress);
                using var actual = new MemoryStream(); decoder.CopyTo(actual); Assert.Equal(expected, actual.ToArray());
            }
        }
    }
}
#endif
