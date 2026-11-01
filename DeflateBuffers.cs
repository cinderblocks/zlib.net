using System;
using System.Threading;

namespace ComponentAce.Compression.Libs.zlib
{
    /// <summary>A bounded cache of default-size compressor buffers, independent of codec state.</summary>
    internal sealed class DeflateBuffers
    {
        // At most one MiB of payload is cached process-wide. Custom window/memory sizes bypass this cache.
        private static readonly DeflateBuffers?[] slots = new DeflateBuffers?[4];
        internal readonly byte[] Window = new byte[65536], Pending = new byte[65536];
        internal readonly short[] Previous = new short[32768], Heads = new short[32768];

        internal static DeflateBuffers Rent()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                var buffers = Interlocked.Exchange(ref slots[i], null);
                if (buffers != null) { return buffers; }
            }
            return new DeflateBuffers();
        }

        internal static void Return(DeflateBuffers buffers)
        {
            // Do not publish prior callers' data or dictionary/hash state to another lease.
            Array.Clear(buffers.Window, 0, buffers.Window.Length);
            Array.Clear(buffers.Pending, 0, buffers.Pending.Length);
            Array.Clear(buffers.Previous, 0, buffers.Previous.Length);
            Array.Clear(buffers.Heads, 0, buffers.Heads.Length);
            for (int i = 0; i < slots.Length; i++)
                { if (Interlocked.CompareExchange(ref slots[i], buffers, null) == null) { return; } }
            // The cache is full; the bundle is left for collection rather than growing the cache.
        }
    }
}
