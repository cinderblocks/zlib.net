using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Running;
using ComponentAce.Compression.Libs.zlib;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

public static class TestData
{
    private static readonly string[] Words =
    {
        "the", "of", "and", "compression", "stream", "buffer", "window", "deflate", "inflate", "huffman", "literal", "length",
        "distance", "block", "table", "symbol", "code", "tree", "match", "hash", "chain", "dictionary", "byte", "bit", "checksum",
        "function", "return", "int", "static", "void", "public", "class", "namespace", "using", "if", "else", "while", "for",
    };

    /// <summary>Text-like, moderately compressible data (~3x with zlib level 6).</summary>
    public static byte[] Text(int length)
    {
        var rng = new Random(1234);
        var sb = new StringBuilder(length + 32);
        while (sb.Length < length)
        {
            sb.Append(Words[rng.Next(Words.Length)]);
            sb.Append(rng.Next(12) == 0 ? ".\n" : rng.Next(25) == 0 ? "_" + rng.Next(1000) + " " : " ");
        }
        return Encoding.ASCII.GetBytes(sb.ToString(0, length));
    }

    /// <summary>Nearly incompressible data.</summary>
    public static byte[] Random(int length)
    {
        var data = new byte[length];
        new Random(99).NextBytes(data);
        return data;
    }
}

[MemoryDiagnoser]
public class CompressBenchmarks
{
    private byte[] data;

    [Params("Text", "Random")]
    public string Kind;

    [Params(1, 6, 9)]
    public int Level;

    [GlobalSetup]
    public void Setup() => data = Kind == "Text" ? TestData.Text(4 << 20) : TestData.Random(4 << 20);

    [Benchmark(Baseline = true)]
    public long Platform()
    {
        var level = Level == 1 ? CompressionLevel.Fastest : Level == 9 ? CompressionLevel.SmallestSize : CompressionLevel.Optimal;
        var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, level, leaveOpen: true))
            z.Write(data, 0, data.Length);
        return ms.Length;
    }

    [Benchmark]
    public long ZlibNet()
    {
        var ms = new MemoryStream();
        using (var z = new ZOutputStream(ms, Level, leaveOpen: true))
            z.Write(data, 0, data.Length);
        return ms.Length;
    }
}

[MemoryDiagnoser]
public class DecompressBenchmarks
{
    private byte[] compressed;
    private int expected;

    [Params("Text", "Random")]
    public string Kind;

    [GlobalSetup]
    public void Setup()
    {
        var data = Kind == "Text" ? TestData.Text(4 << 20) : TestData.Random(4 << 20);
        expected = data.Length;
        var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data, 0, data.Length);
        compressed = ms.ToArray();
    }

    [Benchmark(Baseline = true)]
    public long Platform()
    {
        using var z = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);
        return Drain(z);
    }

    [Benchmark]
    public long ZlibNetInputStream()
    {
        using var z = new ZInputStream(new MemoryStream(compressed));
        return Drain(z);
    }

    [Benchmark]
    public long ZlibNetOutputStream()
    {
        var sink = new CountingStream();
        using (var z = new ZOutputStream(sink, leaveOpen: true))
            z.Write(compressed, 0, compressed.Length);
        return sink.Count;
    }

    private static long Drain(Stream s)
    {
        var buf = new byte[81920];
        long total = 0;
        int n;
        while ((n = s.Read(buf, 0, buf.Length)) > 0)
            total += n;
        return total;
    }
}

[MemoryDiagnoser]
public class SmallPayloadBenchmarks
{
    private byte[] data;
    private byte[] compressed;

    [GlobalSetup]
    public void Setup()
    {
        data = TestData.Text(2000);
        var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data, 0, data.Length);
        compressed = ms.ToArray();
    }

    [Benchmark]
    public long CompressSmall()
    {
        var sink = new CountingStream();
        using (var z = new ZOutputStream(sink, 6, leaveOpen: true))
            z.Write(data, 0, data.Length);
        return sink.Count;
    }

    [Benchmark]
    public long DecompressSmall()
    {
        var sink = new CountingStream();
        using (var z = new ZOutputStream(sink, leaveOpen: true))
            z.Write(compressed, 0, compressed.Length);
        return sink.Count;
    }
}

public sealed class CountingStream : Stream
{
    public long Count;
    public override void Write(byte[] buffer, int offset, int count) => Count += count;
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

[MemoryDiagnoser]
public class ChecksumBenchmarks
{
    private byte[] data;

    [GlobalSetup]
    public void Setup() => data = TestData.Text(4 << 20);

    [Benchmark]
    public long Adler32_4MB() => ComponentAce.Compression.Libs.zlib.Adler32.adler32(1, data, 0, data.Length);
}

[EventPipeProfiler(BenchmarkDotNet.Diagnosers.EventPipeProfile.CpuSampling)]
public class ProfileInflate
{
    private byte[] compressed;
    [GlobalSetup]
    public void Setup()
    {
        var data = TestData.Text(4 << 20);
        var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data, 0, data.Length);
        compressed = ms.ToArray();
    }

    [Benchmark]
    public long Inflate()
    {
        var sink = new CountingStream();
        using (var z = new ZOutputStream(sink, leaveOpen: true))
            z.Write(compressed, 0, compressed.Length);
        return sink.Count;
    }
}

[EventPipeProfiler(BenchmarkDotNet.Diagnosers.EventPipeProfile.CpuSampling)]
public class ProfileDeflate
{
    private byte[] data;

    [Params("Text", "Random")]
    public string Kind;

    [Params(1, 6)]
    public int Level;

    [GlobalSetup]
    public void Setup() => data = Kind == "Text" ? TestData.Text(4 << 20) : TestData.Random(4 << 20);

    [Benchmark]
    public long Deflate()
    {
        var sink = new CountingStream();
        using (var z = new ZOutputStream(sink, Level, leaveOpen: true))
            z.Write(data, 0, data.Length);
        return sink.Count;
    }
}

[MemoryDiagnoser]
public class RawInflateBenchmarks
{
    private byte[] raw, zlibWrapped;

    [Params(4096, 32768, 262144)]
    public int OutBuf;

    [GlobalSetup]
    public void Setup()
    {
        var data = TestData.Text(4 << 20);
        var ms = new MemoryStream();
        using (var z = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data, 0, data.Length);
        raw = ms.ToArray();
        ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data, 0, data.Length);
        zlibWrapped = ms.ToArray();
    }

    private long Run(byte[] input, int windowBits)
    {
        var z = new ZStream();
        if (windowBits < 0) z.inflateInit(windowBits); else z.inflateInit();
        var buf = new byte[OutBuf];
        z.next_in = input; z.next_in_index = 0; z.avail_in = input.Length;
        long total = 0;
        while (true)
        {
            z.next_out = buf; z.next_out_index = 0; z.avail_out = buf.Length;
            int err = z.inflate(zlibConst.Z_NO_FLUSH);
            total += buf.Length - z.avail_out;
            if (err == zlibConst.Z_STREAM_END) break;
            if (err != zlibConst.Z_OK) throw new Exception("err " + err);
        }
        return total;
    }

    [Benchmark] public long RawNoChecksum() => Run(raw, -15);
    [Benchmark] public long ZlibWithAdler() => Run(zlibWrapped, 15);
}
