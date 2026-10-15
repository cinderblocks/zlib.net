using System;
using System.Collections.Generic;
using System.IO;
using ComponentAce.Compression.Libs.zlib;

namespace zlib.net.Fuzz
{
    /// <summary>Generates a small deterministic seed corpus, so no binary files need to live in the repository.</summary>
    internal static class Corpus
    {
        public static void Write(string directory)
        {
            var inflate = Directory.CreateDirectory(Path.Combine(directory, "inflate")).FullName;
            var roundtrip = Directory.CreateDirectory(Path.Combine(directory, "roundtrip")).FullName;

            var samples = new Dictionary<string, byte[]>
            {
                ["empty"] = Array.Empty<byte>(),
                ["one-byte"] = new byte[] { 42 },
                ["text"] = Text(2000),
                ["zeros"] = new byte[3000],
                ["random"] = Random(1500),
                ["periodic"] = Periodic(2500, 3),
            };

            foreach (var sample in samples)
            {
                // Level 0 gives stored blocks, 1 and 6 give fixed and dynamic Huffman blocks.
                foreach (int level in new[] { 0, 1, 6, 9 })
                {
                    byte[] packed = Deflate(sample.Value, level);
                    File.WriteAllBytes(Path.Combine(inflate, $"{sample.Key}-l{level}"), Prefix(0x00, packed));
                    if (level == 6)
                        File.WriteAllBytes(Path.Combine(inflate, $"{sample.Key}-l{level}-1by1"), Prefix(0x04, packed));
                    File.WriteAllBytes(Path.Combine(roundtrip, $"{sample.Key}-l{level}"), Prefix(new byte[] { (byte)(level + 1), 3, 0 }, sample.Value));
                }

                File.WriteAllBytes(Path.Combine(roundtrip, $"{sample.Key}-chunked-sync"), Prefix(new byte[] { 7, 1, 1 }, sample.Value));
                File.WriteAllBytes(Path.Combine(roundtrip, $"{sample.Key}-bytewise-full"), Prefix(new byte[] { 7, 0, 2 }, sample.Value));
            }

            // Hand-built malformed streams.
            File.WriteAllBytes(Path.Combine(inflate, "block-type-3"), Prefix(0x00, new byte[] { 0x78, 0x9C, 0x07 }));
            File.WriteAllBytes(Path.Combine(inflate, "bad-header"), Prefix(0x00, new byte[] { 0x78, 0x9D, 0x03, 0x00, 0, 0, 0, 1 }));
            File.WriteAllBytes(Path.Combine(inflate, "stored-len-mismatch"), Prefix(0x00, new byte[] { 0x78, 0x9C, 0x01, 0x05, 0x00, 0x00, 0x00 }));
            File.WriteAllBytes(Path.Combine(inflate, "distance-too-far"), Prefix(0x00, new byte[] { 0x78, 0x9C, 0x03, 0x01, 0x00 }));

            // A truncated stream.
            byte[] whole = Deflate(samples["text"], 6);
            var truncated = new byte[whole.Length / 2];
            Array.Copy(whole, truncated, truncated.Length);
            File.WriteAllBytes(Path.Combine(inflate, "truncated"), Prefix(0x00, truncated));
        }

        private static byte[] Prefix(byte first, byte[] rest) => Prefix(new[] { first }, rest);

        private static byte[] Prefix(byte[] head, byte[] rest)
        {
            var result = new byte[head.Length + rest.Length];
            head.CopyTo(result, 0);
            rest.CopyTo(result, head.Length);
            return result;
        }

        private static byte[] Deflate(byte[] input, int level)
        {
            var ms = new MemoryStream();
            using (var zout = new ZOutputStream(ms, level, leaveOpen: true))
                zout.Write(input, 0, input.Length);
            return ms.ToArray();
        }

        private static byte[] Text(int length)
        {
            string[] words = { "the", "quick", "brown", "fox", "jumps", "over", "lazy", "dog", "deflate", "window", "match", "literal" };
            var sb = new System.Text.StringBuilder();
            var rng = new Random(1);
            while (sb.Length < length)
                sb.Append(words[rng.Next(words.Length)]).Append(' ');
            return System.Text.Encoding.ASCII.GetBytes(sb.ToString(0, length));
        }

        private static byte[] Random(int length)
        {
            var data = new byte[length];
            new Random(2).NextBytes(data);
            return data;
        }

        private static byte[] Periodic(int length, int period)
        {
            var data = new byte[length];
            for (int i = 0; i < length; i++)
                data[i] = (byte)(i % period);
            return data;
        }
    }
}
