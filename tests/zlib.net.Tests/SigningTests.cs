using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using ComponentAce.Compression.Libs.zlib;
using Xunit;
namespace zlib.net.Tests
{
    public class SigningTests
    {
        [Fact]
        public void LibraryAndFriendAssembliesHaveStableStrongNameIdentity()
        {
            var library = typeof(ZStream).Assembly.GetName();
            Assert.Equal("d3655c1a090d0873", BitConverter.ToString(library.GetPublicKeyToken()).Replace("-", "").ToLowerInvariant());
            Assert.Equal(library.GetPublicKey(), typeof(SigningTests).Assembly.GetName().GetPublicKey());
            var friends = typeof(ZStream).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().ToArray();
            Assert.Equal(2, friends.Length);
            Assert.All(friends, f => Assert.Contains(", PublicKey=" + BitConverter.ToString(library.GetPublicKey()).Replace("-", ""), f.AssemblyName, StringComparison.OrdinalIgnoreCase));
        }

#if NET8_0_OR_GREATER
        [Fact]
        public void LibraryContainsAFullValidStrongNameSignature()
        {
            // Verify the PE signature independently: neither reflection nor .NET Core loading verifies it.
            byte[] image = File.ReadAllBytes(typeof(ZStream).Assembly.Location);
            int pe = BitConverter.ToInt32(image, 0x3c);
            int sections = BitConverter.ToUInt16(image, pe + 6);
            int optional = pe + 24;
            int headerSize = BitConverter.ToInt32(image, optional + 60);
            int directory = optional + (BitConverter.ToUInt16(image, optional) == 0x20b ? 112 : 96);
            int sectionTable = optional + BitConverter.ToUInt16(image, pe + 20);
            Func<int, int> offset = rva =>
            {
                for (int i = 0; i < sections; i++)
                {
                    int s = sectionTable + i * 40;
                    int va = BitConverter.ToInt32(image, s + 12);
                    int size = Math.Max(BitConverter.ToInt32(image, s + 8), BitConverter.ToInt32(image, s + 16));
                    if (rva >= va && rva < va + size) return BitConverter.ToInt32(image, s + 20) + rva - va;
                }
                throw new Exception("Invalid RVA.");
            };
            int cli = offset(BitConverter.ToInt32(image, directory + 14 * 8));
            Assert.NotEqual(0, BitConverter.ToInt32(image, cli + 16) & 8); // StrongNameSigned flag
            int signatureOffset = offset(BitConverter.ToInt32(image, cli + 32));
            int signatureSize = BitConverter.ToInt32(image, cli + 36);
            byte[] signature = image.Skip(signatureOffset).Take(signatureSize).Reverse().ToArray();
            Array.Clear(image, optional + 64, 4); // checksum
            Array.Clear(image, directory + 4 * 8, 8); // certificate directory
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            hash.AppendData(image, 0, sectionTable + sections * 40); // exclude file-alignment padding
            for (int i = 0; i < sections; i++)
            {
                int section = sectionTable + i * 40;
                int start = BitConverter.ToInt32(image, section + 20);
                int size = BitConverter.ToInt32(image, section + 16);
                if (signatureOffset >= start && signatureOffset < start + size)
                {
                    hash.AppendData(image, start, signatureOffset - start);
                    int after = signatureOffset + signatureSize;
                    hash.AppendData(image, after, start + size - after);
                }
                else hash.AppendData(image, start, size);
            }
            byte[] key = typeof(ZStream).Assembly.GetName().GetPublicKey();
            int modulusSize = BitConverter.ToInt32(key, 24) / 8;
            using var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters { Modulus = key.Skip(32).Take(modulusSize).Reverse().ToArray(), Exponent = new byte[] { 1, 0, 1 } });
            Assert.True(rsa.VerifyHash(hash.GetHashAndReset(), signature, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1));
        }
#endif
    }
}
