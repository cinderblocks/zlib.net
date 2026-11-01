using System;
using ComponentAce.Compression.Libs.zlib;
using Xunit;
namespace zlib.net.Tests
{
    public class MatchCopyTests
    {
        [Fact]
        public void BulkMatchesAgreeWithBytewiseExpansionIncludingWindowWrap()
        {
            const int size = 32768;
            var initial = TestData.Random(size);
            var z = new ZStream();
            var blocks = new InfBlocks(z, null, size);
            foreach (int position in new[] { 0, 1, 7, 257, size - 258, size - 7, size - 1 })
                foreach (int distance in new[] { 1, 2, 3, 7, 31, 257, size - 1, size })
                    foreach (int length in new[] { 1, 2, 7, 31, 258 })
                    {
                        int count = Math.Min(length, size - position);
                        var expected = (byte[])initial.Clone();
                        for (int i = 0; i < count; i++)
                            expected[position + i] = expected[(position + i - distance + size) % size];
                        Array.Copy(initial, blocks.window, size);
                        blocks.CopyMatch(position, distance, count);
                        Assert.Equal(expected, blocks.window);
                    }
        }
    }
}
