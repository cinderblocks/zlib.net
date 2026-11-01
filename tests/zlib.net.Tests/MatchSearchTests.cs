using System;
using ComponentAce.Compression.Libs.zlib;
using Xunit;

namespace zlib.net.Tests
{
    public class MatchSearchTests
    {
        [Fact]
        public void MatchSearchFindsEveryMismatchByteAndBitIncludingUnalignedWords()
        {
            var z = new ZStream(); z.deflateInit(6);
            try
            {
                var d = z.dstate;
                byte[] pattern = TestData.Random(259);
                for (int alignment = 0; alignment < 8; alignment++)
                for (int mismatch = 3; mismatch <= 258; mismatch++)
                for (int bit = 0; bit < 8; bit++)
                {
                    int scan = 1000 + alignment, match = 300 + alignment;
                    Array.Copy(pattern, 0, d.window, scan, pattern.Length);
                    Array.Copy(pattern, 0, d.window, match, pattern.Length);
                    d.window[match + mismatch] ^= (byte)(1 << bit);
                    d.strstart = scan; d.lookahead = 258; d.prev_length = 2;
                    d.prev[match & 32767] = 0;
                    Assert.Equal(mismatch, d.longest_match(match));
                }
            }
            finally { z.deflateEnd(); }
        }
    }
}
