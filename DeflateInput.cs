using System;

namespace ComponentAce.Compression.Libs.zlib
{
    // Synchronous, call-scoped input. A Span never escapes into persistent codec state.
    internal readonly ref struct DeflateInput
    {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
        private readonly ReadOnlySpan<byte> data;
        internal DeflateInput(ReadOnlySpan<byte> data) { this.data = data; }
        internal DeflateInput(byte[]? data) { this.data = data; }
        internal void CopyTo(int offset, byte[] destination, int start, int count) =>
            data.Slice(offset, count).CopyTo(destination.AsSpan(start, count));
#else
        private readonly byte[]? data;
        internal DeflateInput(byte[]? data) { this.data = data; }
        internal void CopyTo(int offset, byte[] destination, int start, int count) =>
            Array.Copy(data!, offset, destination, start, count);
#endif
    }
}
