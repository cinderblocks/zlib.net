using System;
using System.IO;
using System.IO.Compression;
using ComponentAce.Compression.Libs.zlib;

namespace zlib.net.Fuzz
{
    /// <summary>
    /// Fuzz targets. A target throws if, and only if, it finds a bug: a <see cref="ZStreamException"/> on malformed
    /// input is the library working as designed and is swallowed, anything else escapes so the fuzzer records it.
    /// </summary>
    internal static class Targets
    {
        // Inputs larger than this only slow the fuzzer down.
        public const int MaxInput = 1 << 20;

        // A valid decompression bomb can legitimately take minutes to inflate one byte at a time. Once this much output
        // has been checked the run is stopped, so the fuzzer's timeout only fires on genuine hangs.
        private const long StopAfter = 8 << 20;

        private sealed class EnoughOutputException : Exception
        {
        }

        public static byte[] ReadAll(Stream input)
        {
            using var ms = new MemoryStream();
            var buffer = new byte[8192];
            int n;
            while (ms.Length < MaxInput && (n = input.Read(buffer, 0, buffer.Length)) > 0)
                ms.Write(buffer, 0, Math.Min(n, MaxInput - (int)ms.Length));
            return ms.ToArray();
        }

        /// <summary>
        /// Arbitrary bytes as a zlib stream. The first byte picks how the data is fed to the library, the rest is the
        /// stream. Must never hang, crash, run away in memory or throw anything but <see cref="ZStreamException"/>.
        /// </summary>
        public static void Inflate(byte[] input)
        {
            if (input.Length == 0)
                return;

            int config = input[0];
            var data = new byte[input.Length - 1];
            Array.Copy(input, 1, data, 0, data.Length);

            // Deflate cannot expand data by more than about 1032:1, so anything beyond this is a runaway (a bug).
            long limit = 1100L * data.Length + 4096;

            int chunk = config % 4 == 0 ? 1 : config % 4 == 1 ? 7 : config % 4 == 2 ? 512 : Math.Max(1, data.Length);
            try
            {
                using var zout = new ZOutputStream(new LimitedSink(limit, StopAfter), leaveOpen: true);
                for (int off = 0; off < data.Length; off += chunk)
                    zout.Write(data, off, Math.Min(chunk, data.Length - off));
                zout.Finish();
            }
            catch (ZStreamException)
            {
            }
            catch (EnoughOutputException)
            {
            }

            int readSize = (config >> 2) % 3 == 0 ? 1 : (config >> 2) % 3 == 1 ? 100 : 16 * 1024;
            try
            {
                using var zin = new ZInputStream(new MemoryStream(data));
                var sink = new LimitedSink(limit, StopAfter);
                var buffer = new byte[readSize];
                int n;
                while ((n = zin.Read(buffer, 0, buffer.Length)) > 0)
                    sink.Write(buffer, 0, n);
            }
            catch (ZStreamException)
            {
            }
            catch (EnoughOutputException)
            {
            }
        }

        /// <summary>
        /// Arbitrary bytes as <i>data to compress</i>. The first three bytes pick the compression level, the chunking and
        /// the flush mode. Whatever comes out must inflate back to the input, with this library and with the runtime's zlib.
        /// This exercises the compressor's match finder, window sliding and checksum code, which inflate fuzzing never reaches.
        /// </summary>
        public static void RoundTrip(byte[] input)
        {
            if (input.Length < 3)
                return;

            int level = input[0] % 11 - 1; // -1 (default) .. 9
            int chunk = input[1] % 4 == 0 ? 1 : input[1] % 4 == 1 ? 333 : input[1] % 4 == 2 ? 4096 : int.MaxValue;
            int flush = (input[2] % 3) switch { 0 => zlibConst.Z_NO_FLUSH, 1 => zlibConst.Z_SYNC_FLUSH, _ => zlibConst.Z_FULL_FLUSH };
            var data = new byte[input.Length - 3];
            Array.Copy(input, 3, data, 0, data.Length);

            var compressed = new MemoryStream();
            using (var zout = new ZOutputStream(compressed, level, leaveOpen: true) { FlushMode = flush })
            {
                for (int off = 0; off < data.Length; off += chunk)
                    zout.Write(data, off, (int)Math.Min((long)chunk, data.Length - off));
                zout.Finish();
            }
            var packed = compressed.ToArray();

            using (var zin = new ZInputStream(new MemoryStream(packed)))
            {
                var restored = new MemoryStream();
                zin.CopyTo(restored);
                Require(restored.ToArray().AsSpan().SequenceEqual(data), $"round trip mismatch (level {level}, chunk {chunk}, flush {flush}, {data.Length} bytes)");
            }

            using (var platform = new ZLibStream(new MemoryStream(packed), CompressionMode.Decompress))
            {
                var restored = new MemoryStream();
                platform.CopyTo(restored);
                Require(restored.ToArray().AsSpan().SequenceEqual(data), $"the runtime's zlib decoded our output differently (level {level}, chunk {chunk}, flush {flush}, {data.Length} bytes)");
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private sealed class LimitedSink : Stream
        {
            private readonly long limit;
            private readonly long stopAfter;
            private long written;
            public LimitedSink(long limit, long stopAfter) { this.limit = limit; this.stopAfter = stopAfter; }

            public override void Write(byte[] buffer, int offset, int count)
            {
                written += count;
                if (written > limit)
                    throw new InvalidOperationException($"output exceeded {limit} bytes");
                if (written > stopAfter)
                    throw new EnoughOutputException();
            }

            public override void Flush() { }
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
