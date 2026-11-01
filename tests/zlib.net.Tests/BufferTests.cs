using System;
using System.IO;
using ComponentAce.Compression.Libs.zlib;
using Xunit;
namespace zlib.net.Tests
{
    public class BufferTests
    {
        private sealed class CountingSink : MemoryStream
        {
            public int Writes;
            public override void Write(byte[] b, int offset, int count) { Writes++; base.Write(b, offset, count); }
        }

        [Fact]
        public void DefaultBufferReducesSinkWriteCalls()
        {
            byte[] data = TestData.Random(128 * 1024);
            var sink = new CountingSink();
            var packed = CompletionTests.Pack(data);
            using (var z = new ZOutputStream(sink, true)) z.Write(packed, 0, packed.Length);
            Assert.Equal(data, sink.ToArray());
            Assert.InRange(sink.Writes, 8, 16); // A 4 KB buffer needed at least 32 writes.
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4096)]
        [InlineData(16384)]
        [InlineData(32768)]
        public void ConfigurableBuffersPreserveCompressedAndDecompressedBytes(int size)
        {
            byte[] data = TestData.Mixed(40000);
            var packed = new MemoryStream();
            using (var z = new ZOutputStream(packed, 6, true, size)) z.Write(data, 0, data.Length);
            Assert.Equal(CompletionTests.Pack(data), packed.ToArray());
            var restored = new MemoryStream();
            using (var z = new ZOutputStream(restored, true, size))
                z.Write(packed.ToArray(), 0, (int)packed.Length);
            Assert.Equal(data, restored.ToArray());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void InvalidBufferSizesAreRejected(int size)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ZOutputStream(Stream.Null, true, size));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ZOutputStream(Stream.Null, 6, true, size));
        }
    }
}
