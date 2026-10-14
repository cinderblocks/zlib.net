using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ComponentAce.Compression.Libs.zlib;
using Xunit;

namespace zlib.net.Tests
{
    /// <summary>
    /// Pins the exact bytes the deflater produces. Performance work must not change the output (and therefore the
    /// compression ratio); a deliberate change to the compressor should regenerate these with GOLDEN_PRINT=1.
    /// </summary>
    public class GoldenOutputTests
    {
        private static readonly Dictionary<string, byte[]> Inputs = new Dictionary<string, byte[]>
        {
            ["text"] = TestData.Text(300_000),
            ["random"] = TestData.Random(100_000),
            ["zeros"] = new byte[100_000],
            ["period3"] = TestData.Periodic(100_000, 3),
            ["mixed"] = TestData.Mixed(250_000),
            ["tiny"] = Encoding.ASCII.GetBytes("hello hello hello"),
        };

        private static string Hash(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(data);
                var sb = new StringBuilder();
                foreach (var b in h) sb.Append(b.ToString("x2"));
                return sb.ToString().Substring(0, 24);
            }
        }

        private static string Compute(string name, int level, int chunk)
        {
            var ms = new MemoryStream();
            using (var zout = new ZOutputStream(ms, level, leaveOpen: true))
            {
                var input = Inputs[name];
                if (chunk == 0)
                    zout.Write(input, 0, input.Length);
                else
                    for (int off = 0; off < input.Length; off += chunk)
                        zout.Write(input, off, Math.Min(chunk, input.Length - off));
            }
            return $"{ms.Length}:{Hash(ms.ToArray())}";
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (var name in new[] { "text", "random", "zeros", "period3", "mixed", "tiny" })
                foreach (var level in new[] { 0, 1, 4, 6, 9 })
                    yield return new object[] { name, level, 0 };
            yield return new object[] { "text", 6, 1000 };
            yield return new object[] { "mixed", 1, 4097 };
        }

        private static readonly Dictionary<string, string> Expected = new Dictionary<string, string>
        {
            // GOLDEN-BEGIN
            ["mixed/L0/c0"] = "250041:e7be476677d8b23c7d13dd9b",
            ["mixed/L1/c0"] = "92447:1e69026eaf64c46e43d921dd",
            ["mixed/L1/c4097"] = "92447:1e69026eaf64c46e43d921dd",
            ["mixed/L4/c0"] = "91956:a4c79a169dd3c72aea5aa08e",
            ["mixed/L6/c0"] = "91939:c21fbe1a3d142b935020b449",
            ["mixed/L9/c0"] = "91803:0859bfc7e8f0c09bd1222a74",
            ["period3/L0/c0"] = "100021:907b8ba41dbe937a5a5988a7",
            ["period3/L1/c0"] = "464:adb021d3696ac196ab055b1a",
            ["period3/L4/c0"] = "123:55f2df52b1dd18360626ecdb",
            ["period3/L6/c0"] = "123:8e7bf54f3af268af89514915",
            ["period3/L9/c0"] = "123:15c0a2a5f205d4f5333ff952",
            ["random/L0/c0"] = "100021:2f19b54a1067edb3b075cc6f",
            ["random/L1/c0"] = "100041:07b40d5eddb89d4841101769",
            ["random/L4/c0"] = "100041:a016cb29077b964f16ea0063",
            ["random/L6/c0"] = "100041:4d41ba23815ef9c76c7f93af",
            ["random/L9/c0"] = "100041:10fba2bc99bb6c3631ce102e",
            ["text/L0/c0"] = "300051:7805c1030bd91ddcf8bb736e",
            ["text/L1/c0"] = "69757:306bf49dbc65d1d53d6706af",
            ["text/L4/c0"] = "58586:8a87992053dd6b7fc95f6506",
            ["text/L6/c0"] = "48944:00043ebd496df0140406b600",
            ["text/L6/c1000"] = "48944:00043ebd496df0140406b600",
            ["text/L9/c0"] = "48080:63b92942a35f2dbab6450432",
            ["tiny/L0/c0"] = "28:88d4c702814442f619232e4a",
            ["tiny/L1/c0"] = "16:a8dd09a49f9395d116de51c9",
            ["tiny/L4/c0"] = "16:fc118b84662893f6027c60ed",
            ["tiny/L6/c0"] = "16:98437c7b782ac27a3898e0b4",
            ["tiny/L9/c0"] = "16:6aaa41661d84369f624b9ca1",
            ["zeros/L0/c0"] = "100021:11743e0b666c818145b0541e",
            ["zeros/L1/c0"] = "460:d6f472c50d475d20a5875995",
            ["zeros/L4/c0"] = "120:29063f0e6b702e9dc461f005",
            ["zeros/L6/c0"] = "120:a2ea4b930bf1fb9b300dc15f",
            ["zeros/L9/c0"] = "120:345c550fa604b6698d68d993",
            // GOLDEN-END
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public void OutputIsUnchanged(string name, int level, int chunk)
        {
            var key = $"{name}/L{level}/c{chunk}";
            var actual = Compute(name, level, chunk);
            if (Environment.GetEnvironmentVariable("GOLDEN_PRINT") == "1")
            {
                File.AppendAllText(Environment.GetEnvironmentVariable("GOLDEN_FILE"), $"            [\"{key}\"] = \"{actual}\",\n");
                return;
            }
            Assert.True(Expected.TryGetValue(key, out var expected), $"no golden value for {key}: {actual}");
            Assert.Equal(expected, actual);
        }
    }
}
