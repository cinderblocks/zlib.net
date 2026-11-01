using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using ComponentAce.Compression.Libs.zlib;

// This runner intentionally uses only public APIs present in v2.0.2, except optional Reset
// discovered once by reflection. Point LibraryPath at a saved baseline DLL to compare versions.
var results = new List<Result>();
Console.WriteLine($"{RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}");
var zeros = new byte[1 << 20];
var text = new byte[1 << 20];
var rng = new Random(123);
for (int i = 0; i < text.Length; i++) text[i] = (byte)rng.Next(32, 64);
var small = text.AsSpan(0, 2048).ToArray();
byte[] Pack(byte[] data, bool chunks = false, CompressionLevel level = CompressionLevel.Optimal)
{
    using var target = new MemoryStream();
    using (var z = new ZLibStream(target, level, true))
    {
        if (!chunks) z.Write(data);
        else for (int i = 0; i < data.Length; i += 8192) { z.Write(data.AsSpan(i, Math.Min(8192, data.Length - i))); z.Flush(); }
    }
    return target.ToArray();
}
var fixedSmall = Pack(System.Text.Encoding.ASCII.GetBytes("request"));
var stored = Pack(text, level: CompressionLevel.NoCompression);
var compressedZeros = Pack(zeros);
var dynamicMany = Pack(text, chunks: true);
var compressedText = Pack(text);
var oneBlock = EmptyBlocks(1); var manyBlocks = EmptyBlocks(10000);
// Validate fixtures once outside timing; the timed drain uses byte counts to avoid hashing overhead.
foreach (var fixture in new[] { (fixedSmall, System.Text.Encoding.ASCII.GetBytes("request")), (stored, text), (compressedZeros, zeros), (dynamicMany, text), (compressedText, text), (oneBlock, Array.Empty<byte>()), (manyBlocks, Array.Empty<byte>()) })
{
    using var z = new ZInputStream(new MemoryStream(fixture.Item1));
    using var actual = new MemoryStream(); z.CopyTo(actual);
    if (!actual.ToArray().AsSpan().SequenceEqual(fixture.Item2)) throw new InvalidOperationException("Invalid benchmark output.");
}
var buffer = new byte[4096];
long Drain(byte[] packed, int size = 4096, bool asyncMemory = false, bool asyncArray = false)
{
    using var z = new ZInputStream(new MemoryStream(packed));
    long total = 0; int n;
    while ((n = asyncMemory ? z.ReadAsync(buffer.AsMemory(0, size)).GetAwaiter().GetResult() :
                asyncArray ? z.ReadAsync(buffer, 0, size).GetAwaiter().GetResult() : z.Read(buffer, 0, size)) > 0) total += n;
    return total;
}
long Compress(byte[] data, bool span = false)
{
    var sink = new Sink();
    using (var z = new ZOutputStream(sink, 6, true)) { if (span) z.Write(data.AsSpan()); else z.Write(data, 0, data.Length); }
    return sink.Count;
}
Measure("compress/empty", () => Compress(Array.Empty<byte>()));
Measure("compress/2KiB", () => Compress(small));
Measure("compress/1MiB-array", () => Compress(text));
Measure("compress/1MiB-span", () => Compress(text, true));
Measure("inflate/fixed-small", () => Drain(fixedSmall));
Measure("inflate/fixed-10000-empty-blocks", () => Drain(manyBlocks));
Measure("inflate/fixed-one-empty-block", () => Drain(oneBlock));
Measure("inflate/dynamic-many-blocks", () => Drain(dynamicMany));
Measure("inflate/stored", () => Drain(stored));
Measure("inflate/zeros-sync", () => Drain(compressedZeros));
Measure("inflate/text-sync", () => Drain(compressedText));
Measure("inflate/zeros-memory-async", () => Drain(compressedZeros, asyncMemory: true));
Measure("inflate/zeros-array-async", () => Drain(compressedZeros, asyncArray: true));
Measure("flush/100-messages", () =>
{
    using var target = new MemoryStream();
    using (var z = new ZOutputStream(target, 6, true)) for (int i = 0; i < 100; i++) { z.Write(small, 0, small.Length); z.Flush(); }
    return target.Length;
});
var resetInfo = typeof(ZOutputStream).GetMethod("Reset", Type.EmptyTypes);
if (resetInfo != null)
{
    var reset = (Action<ZOutputStream>)resetInfo.CreateDelegate(typeof(Action<ZOutputStream>));
    using var z = new ZOutputStream(Stream.Null, 6, true); z.Finish();
    Measure("compress/reused-2KiB", () => { reset(z); z.Write(small, 0, small.Length); z.Finish(); return z.TotalOut; });
    Measure("compress/reused-empty", () => { reset(z); z.Finish(); return z.TotalOut; });
}

Collect();
using (var z = new ZOutputStream(Stream.Null, 6, true))
{
    var weak = WriteLarge(z); Collect(); Console.WriteLine($"retained/caller-input={weak.IsAlive}"); GC.KeepAlive(z);
}
using (var z = new ZInputStream(new MemoryStream(Pack(new byte[8 << 20]))))
{
    var weak = ReadLarge(z); Collect(); Console.WriteLine($"retained/caller-output={weak.IsAlive}"); GC.KeepAlive(z);
}
var sourceRef = DisposedSource(out var wrapper); Collect(); Console.WriteLine($"retained/disposed-source={sourceRef.IsAlive}"); GC.KeepAlive(wrapper);
var sample = Pack(new byte[50000]);
using (var source = new WithheldTrailer(sample))
using (var z = new ZInputStream(source))
{
    int total = 0; var one = new byte[1];
    try { while (z.Read(one, 0, 1) > 0) total++; } catch (IOException) { }
    Console.WriteLine($"latency/decoded-before-trailer-read={total}");
}
using (var target = new MemoryStream())
using (var z = new ZOutputStream(target, 6, true))
{
    z.Write(new byte[] { 1, 2, 3, 4, 5, 6, 7 }); z.Flush();
    using var decoder = new ZLibStream(new MemoryStream(target.ToArray()), CompressionMode.Decompress);
    using var actual = new MemoryStream(); decoder.CopyTo(actual);
    Console.WriteLine($"flush/visible-message-bytes={actual.Length}");
}
var capabilities = new ZOutputStream(Stream.Null, 6, true); capabilities.Finish();
Console.WriteLine($"state/CanWrite-after-Finish={capabilities.CanWrite}"); capabilities.End();
Console.WriteLine($"state/CanWrite-after-End={capabilities.CanWrite}"); capabilities.Dispose();
try { using var z = new ZInputStream(new MemoryStream(fixedSmall)); z.ReadAsync(Memory<byte>.Empty, new CancellationToken(true)).GetAwaiter().GetResult(); Console.WriteLine("cancellation/empty-read=ignored"); }
catch (OperationCanceledException) { Console.WriteLine("cancellation/empty-read=observed"); }
if (args.Length > 0) File.WriteAllText(args[0], JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));

void Measure(string name, Func<long> operation)
{
    var warm = Stopwatch.StartNew(); do { operation(); } while (warm.ElapsedMilliseconds < 350);
    var timings = new List<double>(); long allocated = 0, operations = 0, value = 0;
    for (int sample = 0; sample < 7; sample++)
    {
        int count = 0; long initial = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        do { value = operation(); count++; } while (timer.ElapsedMilliseconds < 100);
        timer.Stop(); allocated += GC.GetAllocatedBytesForCurrentThread() - initial; operations += count;
        timings.Add(timer.Elapsed.TotalMilliseconds / count);
    }
    timings.Sort(); var result = new Result(name, timings[3], allocated / operations, value);
    results.Add(result); Console.WriteLine($"{name}: {result.Milliseconds.ToString("F4", CultureInfo.InvariantCulture)} ms, {result.AllocatedBytes} B/op, result={value}");
}
static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
[MethodImpl(MethodImplOptions.NoInlining)] static WeakReference WriteLarge(ZOutputStream z) { var data = new byte[8 << 20]; z.Write(data, 0, data.Length); return new WeakReference(data); }
[MethodImpl(MethodImplOptions.NoInlining)] static WeakReference ReadLarge(ZInputStream z) { var data = new byte[8 << 20]; z.Read(data, 0, data.Length); return new WeakReference(data); }
[MethodImpl(MethodImplOptions.NoInlining)] static WeakReference DisposedSource(out ZInputStream z) { var data = new byte[8 << 20]; z = new ZInputStream(new MemoryStream(data)); z.Dispose(); return new WeakReference(data); }
static byte[] EmptyBlocks(int count)
{
    var data = new List<byte> { 0x78, 0x9c }; int accumulator = 0, bits = 0;
    void Put(int value, int length) { accumulator |= value << bits; bits += length; while (bits >= 8) { data.Add((byte)accumulator); accumulator >>= 8; bits -= 8; } }
    for (int i = 0; i < count; i++) { Put(i == count - 1 ? 3 : 2, 3); Put(0, 7); }
    if (bits != 0) data.Add((byte)accumulator); data.AddRange(new byte[] { 0, 0, 0, 1 }); return data.ToArray();
}
record Result(string Name, double Milliseconds, long AllocatedBytes, long Value);
class Sink : Stream
{
    public long Count; public override void Write(byte[] data, int offset, int count) => Count += count;
    public override void Flush() { } public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
    public override long Length => Count; public override long Position { get => Count; set => throw new NotSupportedException(); }
    public override int Read(byte[] b, int o, int n) => throw new NotSupportedException(); public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException(); public override void SetLength(long n) => throw new NotSupportedException();
}
class WithheldTrailer : MemoryStream
{
    readonly int firstLength; bool first = true;
    public WithheldTrailer(byte[] data) : base(data) { firstLength = data.Length - 4; }
    public override int Read(byte[] b, int o, int n) { if (!first) throw new IOException("trailer withheld"); first = false; return base.Read(b, o, Math.Min(n, firstLength)); }
}
