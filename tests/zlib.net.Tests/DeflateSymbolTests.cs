using System;
using System.IO;
using System.IO.Compression;
using ComponentAce.Compression.Libs.zlib;
using Xunit;
namespace zlib.net.Tests
{
    public class DeflateSymbolTests
    {
        [Fact]
        public void WorstCaseFixedMatchesCannotOverwriteUnreadSymbols()
        {
            var z = new ZStream(); z.deflateInit(6);
            var state = z.dstate;
            int count = state.lit_bufsize - 1;
            try
            {
                // Each length-257/distance-32768 pair needs 31 bits with fixed Huffman codes.
                // Exercise the encoder's buffer invariant directly, independently of tree selection.
                for (int i = 0; i < count; i++) state._tr_tally(32768, 254);
                state.send_bits(3, 3); // final block, fixed trees
                state.compress_block(StaticTree.static_ltree, StaticTree.static_dtree);
                state.bi_windup();
                var initial = TestData.Random(32768);
                var raw = new MemoryStream();
                raw.Write(new byte[] { 0, 0, 0x80, 0xff, 0x7f }, 0, 5); // non-final stored history
                raw.Write(initial, 0, initial.Length);
                raw.Write(state.pending_buf, 0, state.pending);
                raw.Position = 0;
                using var platform = new DeflateStream(raw, CompressionMode.Decompress);
                var actual = new MemoryStream(); platform.CopyTo(actual);
                byte[] expected = new byte[initial.Length + count * 257];
                for (int i = 0; i < expected.Length; i++) expected[i] = initial[i % initial.Length];
                Assert.Equal(expected, actual.ToArray());
            }
            finally { z.deflateEnd(); }
        }
    }
}
