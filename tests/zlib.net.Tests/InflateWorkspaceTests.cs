using System;
using System.IO;
using ComponentAce.Compression.Libs.zlib;
using Xunit;
namespace zlib.net.Tests
{
    public class InflateWorkspaceTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(6)]
        public void StoredAndFixedBlocksDoNotAllocateDynamicTables(int level)
        {
            var packed = new MemoryStream(); using (var output = new ZOutputStream(packed, level, true)) output.Write(new byte[] { 1, 2, 3 }, 0, 3);
            var z = new ZStream(); z.inflateInit(); z.next_in = packed.ToArray(); z.avail_in = z.next_in.Length;
            z.next_out = new byte[10]; z.avail_out = 10;
            try { Assert.Equal(1, z.inflate(0)); Assert.Null(z.istate.blocks.hufts); }
            finally { z.inflateEnd(); }
        }
    }
}
