using System;
using System.Text;

namespace zlib.net.Tests
{
    /// <summary>Deterministic test inputs that don't depend on the runtime's System.Random implementation.</summary>
    internal static class TestData
    {
        private static readonly string[] Words =
        {
            "the", "of", "and", "compression", "stream", "buffer", "window", "deflate", "inflate", "huffman", "literal",
            "length", "distance", "block", "table", "symbol", "code", "tree", "match", "hash", "chain", "dictionary",
        };

        public sealed class Rng
        {
            private ulong state;
            public Rng(ulong seed) { state = seed * 0x9E3779B97F4A7C15UL + 1; }

            public uint Next()
            {
                // xorshift64*
                state ^= state >> 12; state ^= state << 25; state ^= state >> 27;
                return (uint)((state * 0x2545F4914F6CDD1DUL) >> 32);
            }

            public int Next(int max) => (int)(Next() % (uint)max);
        }

        public static byte[] Random(int length, ulong seed = 1)
        {
            var rng = new Rng(seed);
            var data = new byte[length];
            for (int i = 0; i < length; i++)
                data[i] = (byte)rng.Next();
            return data;
        }

        public static byte[] Text(int length, ulong seed = 2)
        {
            var rng = new Rng(seed);
            var sb = new StringBuilder(length + 16);
            while (sb.Length < length)
            {
                sb.Append(Words[rng.Next(Words.Length)]);
                sb.Append(rng.Next(12) == 0 ? ".\n" : " ");
            }
            return Encoding.ASCII.GetBytes(sb.ToString(0, length));
        }

        /// <summary>A repeating pattern of the given period: period 1 gives distance-1 matches, and so on.</summary>
        public static byte[] Periodic(int length, int period)
        {
            var data = new byte[length];
            for (int i = 0; i < length; i++)
                data[i] = (byte)('a' + i % period);
            return data;
        }

        /// <summary>Alternating compressible and incompressible regions.</summary>
        public static byte[] Mixed(int length, ulong seed = 3)
        {
            var rng = new Rng(seed);
            var data = new byte[length];
            int i = 0;
            while (i < length)
            {
                int run = 1 + rng.Next(3000);
                int kind = rng.Next(3);
                for (int j = 0; j < run && i < length; j++, i++)
                    data[i] = kind == 0 ? (byte)rng.Next() : kind == 1 ? (byte)'x' : (byte)('a' + (i % 5));
            }
            return data;
        }

        /// <summary>One of several data shapes, chosen by index.</summary>
        public static byte[] Shape(int shape, int length, ulong seed)
        {
            switch (shape % 5)
            {
                case 0: return Text(length, seed);
                case 1: return Random(length, seed);
                case 2: return Periodic(length, 1 + (int)(seed % 7));
                case 3: return Mixed(length, seed);
                default: return new byte[length];
            }
        }
    }
}
