using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ComponentAce.Compression.Libs.zlib;
using zlib.net.Tests;

// Compare saved assemblies with the same runner. Internal initialization allows the
// pre-change assembly to exercise memory levels too; reflection is excluded from measurements.
Console.WriteLine($"{RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}");
var results = new List<Result>();
string filter = args.Length > 1 ? args[1] : "";
var dataSets = new Dictionary<string, byte[]>
{
    ["text"] = TestData.Text(1 << 20), ["random"] = TestData.Random(1 << 20),
    ["mixed"] = TestData.Mixed(1 << 20), ["zeros"] = new byte[1 << 20],
    ["small"] = TestData.Text(2048), ["empty"] = Array.Empty<byte>()
};
var output = new byte[1 << 20];
byte[] Compress(byte[] data, int mem = 8, int bits = 15, int level = 6, bool raw = false)
{
    var z = new ZStream(); z.dstate = new Deflate();
    if (z.dstate.deflateInit2(z, level, 8, raw ? -bits : bits, mem, 0) != 0) { throw new Exception("init"); }
    using var sink = new MemoryStream();
    try
    {
        z.next_in = data; z.avail_in = data.Length;
        int status;
        do
        {
            z.next_out = output; z.next_out_index = 0; z.avail_out = output.Length;
            status = z.deflate(4); if (status != 0 && status != 1) { throw new Exception(z.msg); }
            sink.Write(output, 0, z.next_out_index);
        } while (status != 1);
        return sink.ToArray();
    }
    finally { z.deflateEnd(); }
}
long RunCompression(ZStream z, byte[] data)
{
    if (z.deflateReset() != 0) { throw new Exception("reset"); }
    z.next_in = data; z.next_in_index = 0; z.avail_in = data.Length;
    int status;
    do
    {
        z.next_out = output; z.next_out_index = 0; z.avail_out = output.Length;
        status = z.deflate(4); if (status != 0 && status != 1) { throw new Exception(z.msg); }
    } while (status != 1);
    return z.total_out;
}
foreach (var (name, data) in dataSets)
foreach (int mem in new[] { 6, 8 })
{
    string label = $"compress/{name}/mem{mem}";
    if (!label.Contains(filter, StringComparison.Ordinal)) { continue; }
    var packed = Compress(data, mem);
    Validate(data, packed);
    var z = new ZStream(); z.dstate = new Deflate(); z.dstate.deflateInit2(z, 6, 8, 15, mem, 0);
    try
    {
        long payload = z.dstate.window.Length + z.dstate.pending_buf.Length + 2L * (z.dstate.head.Length + z.dstate.prev.Length);
        Measure(label, () => RunCompression(z, data), payload, Convert.ToHexString(SHA256.HashData(packed)));
    }
    finally { z.deflateEnd(); }
}
foreach (var (name, data) in dataSets.Where(p => p.Key is "text" or "random" or "zeros"))
foreach (int level in new[] { 0, 1, 9 })
{
    string label = $"level/{name}/{level}"; if (!label.Contains(filter, StringComparison.Ordinal)) { continue; }
    byte[] packed = Compress(data, level: level); Validate(data, packed);
    var z = new ZStream(); z.deflateInit(level);
    try { Measure(label, () => RunCompression(z, data), 262144, Convert.ToHexString(SHA256.HashData(packed))); }
    finally { z.deflateEnd(); }
}
foreach (var (name, data) in dataSets.Where(p => p.Key is "text" or "random" or "zeros"))
foreach (bool chunks in new[] { false, true })
{ foreach (int size in new[] { 4096, 65536 })
{
    string label = $"inflate/{name}/{(chunks ? "many" : "single")}/{size}";
    if (!label.Contains(filter, StringComparison.Ordinal)) { continue; }
    using var sink = new MemoryStream();
    using (var compressor = new ZLibStream(sink, CompressionLevel.Optimal, true))
        if (!chunks) { compressor.Write(data); }
        else { for (int i = 0; i < data.Length; i += 8192) { compressor.Write(data.AsSpan(i, Math.Min(8192, data.Length - i))); compressor.Flush(); } }
    byte[] packed = sink.ToArray(); Validate(data, packed);
    Measure(label, () =>
    {
        using var z = new ZInputStream(new MemoryStream(packed));
        long total = 0; int read;
        while ((read = z.Read(output, 0, size)) != 0) { total += read; }
        if (total != data.Length) { throw new Exception("length"); }
        return total;
    });
} }
foreach (int group in new[] { 1, 10, 100 })
{
    string label = $"flush/group{group}"; if (!label.Contains(filter, StringComparison.Ordinal)) { continue; }
    Measure(label, () =>
    {
        using var sink = new MemoryStream();
        using (var z = new ZOutputStream(sink, 6, true))
            for (int i = 0; i < 100; i++) { z.Write(dataSets["small"], 0, 2048); if ((i + 1) % group == 0) { z.Flush(); } }
        return sink.Length;
    });
}
foreach (var (name, data) in dataSets.Where(p => p.Key is "small" or "random" or "text"))
foreach (int level in new[] { 0, 6 })
{ foreach (bool span in new[] { false, true })
{
    string label = $"write/{name}/level{level}/{(span ? "span" : "array")}";
    if (!label.Contains(filter, StringComparison.Ordinal)) continue;
    using var z = new ZOutputStream(Stream.Null, level, true); z.Finish();
    Measure(label, () =>
    {
        z.Reset();
        if (span) { z.Write(data.AsSpan()); } else { z.Write(data, 0, data.Length); }
        z.Finish(); return z.TotalOut;
    });
} }
foreach (int size in new[] { 4096, 65536, 1048576 })
{ foreach (bool raw in new[] { false, true })
{
    string label = $"decode-direct/stored/{(raw ? "raw" : "wrapped")}/{size}";
    if (!label.Contains(filter, StringComparison.Ordinal)) continue;
    byte[] data = dataSets["random"];
    using var sink = new MemoryStream();
    using (var packer = new ZLibStream(sink, CompressionLevel.NoCompression, true)) packer.Write(data);
    byte[] packed = sink.ToArray();
    if (raw) { packed = packed.AsSpan(2, packed.Length - 6).ToArray(); }
    var z = new ZStream(); z.inflateInit(raw ? -15 : 15);
    long Decode()
    {
        z.inflateReset(); z.next_in = packed; z.next_in_index = 0; z.avail_in = packed.Length;
        int status;
        do
        {
            z.next_out = output; z.next_out_index = 0; z.avail_out = size;
            status = z.inflate(0); if (status != 0 && status != 1) { throw new Exception(z.msg); }
        } while (status != 1);
        if (z.total_out != data.Length) { throw new Exception("decoded length"); }
        return z.total_out;
    }
    try { Measure(label, Decode); }
    finally { z.inflateEnd(); }
} }
if (args.Length != 0) { File.WriteAllText(args[0], JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })); }

void Measure(string name, Func<long> operation, long payload = 0, string hash = "")
{
    int warmCount = 0;
    var warm = Stopwatch.StartNew(); do { operation(); warmCount++; } while (warm.ElapsedMilliseconds < 1000 || warmCount < 64);
    var times = new List<double>(); var allocations = new List<long>(); long value = 0;
    for (int sample = 0; sample < 7; sample++)
    {
        int count = 0; var timer = new Stopwatch(); long before = GC.GetAllocatedBytesForCurrentThread(); timer.Start();
        do { value = operation(); count++; } while (timer.ElapsedMilliseconds < 100);
        timer.Stop(); allocations.Add((GC.GetAllocatedBytesForCurrentThread() - before) / count);
        times.Add(timer.Elapsed.TotalMilliseconds / count);
    }
    times.Sort(); allocations.Sort(); var result = new Result(name, times[3], allocations[3], value, payload, hash);
    results.Add(result); Console.WriteLine($"{name}: {result.Milliseconds:F4} ms; {result.AllocatedBytes} B; result={value}; buffers={payload}");
}
static void Validate(byte[] data, byte[] packed)
{
    using var decoder = new ZLibStream(new MemoryStream(packed), CompressionMode.Decompress);
    using var actual = new MemoryStream(); decoder.CopyTo(actual);
    if (!actual.ToArray().AsSpan().SequenceEqual(data)) { throw new Exception("platform roundtrip"); }
}
record Result(string Name, double Milliseconds, long AllocatedBytes, long Value, long BufferPayloadBytes, string CompressedSha256);
