using System;
using System.Threading;

namespace ComponentAce.Compression.Libs.zlib
{
    // A single idle default-size window and table: at most 50,048 bytes of cached payload.
    // Custom windows bypass the cache. Arrays belong exclusively to one active decoder.
    internal static class InflateBuffers
    {
        internal const int WindowSize = 32768, TableSize = 4320;
        private static byte[]? window;
        private static int[]? table;

        internal static byte[] RentWindow(int size) => size == WindowSize
            ? Interlocked.Exchange(ref window, null) ?? new byte[WindowSize] : new byte[size];
        internal static int[] RentTable() => Interlocked.Exchange(ref table, null) ?? new int[TableSize];

        internal static void ReturnWindow(byte[]? value)
        {
            if (value == null || value.Length != WindowSize) { return; }
            Array.Clear(value, 0, value.Length);
            Interlocked.CompareExchange(ref window, value, null);
        }

        internal static void ReturnTable(int[]? value)
        {
            if (value == null || value.Length != TableSize) { return; }
            Array.Clear(value, 0, value.Length);
            Interlocked.CompareExchange(ref table, value, null);
        }
    }
}
