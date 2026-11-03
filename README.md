# zlib.net

[![CI](https://github.com/cinderblocks/ZlibCore/actions/workflows/ci.yml/badge.svg)](https://github.com/cinderblocks/ZlibCore/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/ZlibCore.svg)](https://www.nuget.org/packages/ZlibCore)

Managed version of the ZLIB compression library ported to .NET Core

This port is based on the 100% managed version of the library from
[ComponentAce](http://www.componentace.com/zlib_.NET.htm).

## Provenance and upstream fixes

The implementation comes from ComponentAce ZLIB.NET, whose source credits
JCraft's JZlib Java implementation and Lapo Luchini's stream wrappers. The
engine headers identify native zlib 1.1.3 as their base. The exact imported
JZlib snapshot is not recorded; `zlibConst.version()` retains the historical
`1.0.2` API string and does not report the package or native zlib version.

Useful upstream references are [JCraft JZlib](https://www.jcraft.com/jzlib/),
the [JRuby-maintained fork](https://github.com/jruby/jzlib), and [native
zlib](https://github.com/madler/zlib). This port has diverged; fixes must be
checked against its state machine and managed buffers individually.

The raw-DEFLATE terminal Huffman lookup correction follows
[SharpCompress PR #118](https://github.com/adamhathcock/sharpcompress/pull/118):
an available
terminal entry can require fewer bits than the table lookup width, so the
decoder must not demand a dummy byte. Both length and distance lookups have the
correction, with raw-stream regression coverage. Source attribution and
distribution notices are in [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt).

## Installation

```powershell
dotnet add package ZlibCore
```

Versions up to 1.1.0 were published under the misspelled ID
`zlib.net-mutliplatform`. From 2.0.0 the package is `ZlibCore`.

Targets `netstandard2.0`, `netstandard2.1` and `net8.0`. The `Span`/`Memory`
overloads and `DisposeAsync` are available on `netstandard2.1` and `net8.0`.

## Usage

```csharp
using ComponentAce.Compression.Libs.zlib;

// Compress
var compressed = new MemoryStream();
using (var zout = new ZOutputStream(compressed, zlibConst.Z_DEFAULT_COMPRESSION))
{
    zout.Write(data, 0, data.Length);
    zout.Finish();
    // Read the target before ZOutputStream disposes it.
    result = compressed.ToArray();
}

// Decompress
var decompressed = new MemoryStream();
using (var zout = new ZOutputStream(decompressed))
{
    zout.Write(compressedData, 0, compressedData.Length);
    zout.Finish();
    result = decompressed.ToArray();
}

// DisposeAsync finishes without blocking on the target (netstandard2.1 / net8.0).
await using (var zout = new ZOutputStream(target,
    zlibConst.Z_DEFAULT_COMPRESSION, leaveOpen: true))
{
    await zout.WriteAsync(data);
}

await using (var zin = new ZInputStream(source))
{
    int read = await zin.ReadAsync(buffer.AsMemory());
}
```

## Migrating from 1.x

Switch the package reference to `ZlibCore`. The namespace and assembly name are
unchanged. Breaking changes:

- `ZInputStream` is now a `Stream` rather than a `BinaryReader`. `Read(byte[],
  int, int)` returns decompressed data (previously it returned raw bytes
  from the underlying stream) and returns 0 at the end, like any other stream.
`ReadByte()` replaces the no-argument `Read()`, and `ReadAsync` is supported.
  Compress mode (`new ZInputStream(stream, level)`) now works without setting
`FlushMode`.
- Truncated compressed input now throws `ZStreamException` instead of silently
  ending the stream.
- `ZOutputStream.Read`, `Seek`, `SetLength`, `Length` and `Position` throw
  `NotSupportedException` instead of returning 0.
- `ZOutputStream.finish()`/`end()` and `ZInputStream.read()`/`skip()` are
  renamed `Finish()`/`End()`/`Read()`; the old names still work but are marked
  `[Obsolete]`.
- `SupportClass` and `Deflate` were never meant to be public and are now
  internal.
- New: both streams take a `leaveOpen` argument to keep the underlying stream
  open on dispose.

## Stream validation and buffering

- Decompression-mode `ZOutputStream` accepts one zlib stream. Trailing bytes and
  additional members throw `ZStreamException`, including across write
  boundaries. Non-empty compression writes after stream completion throw
  `InvalidOperationException`; repeated `Finish()` / `FinishAsync()` calls are
  safe.
- `Finish()`, `FinishAsync()`, `Dispose()`, and `DisposeAsync()` propagate
  output failures. Disposal still releases codec resources and closes the
  underlying stream unless `leaveOpen` is true. After a failed codec/output
  operation, disposal cleans up without retrying the failed operation.
- Compression constructors validate levels (-1 through 9); writes validate
  buffer ranges before changing codec state.
- The default output buffer is now 16 KB. For example, use `new
  ZOutputStream(target, level: 6, leaveOpen: true, bufferSize: 32768)` to select
  32 KB, or `new ZOutputStream(target, leaveOpen: true, bufferSize: 32768)` to
  decompress with that size.

These error-reporting and assembly-signing changes can affect existing
consumers; rebuild and test when upgrading.

## Flushing and reuse

`ZOutputStream.Flush()` / `FlushAsync()` now sync-flush the active compressor
before flushing the sink. A reader can consume messages already written without
waiting for `Finish()`. Flushing does not end the compressed stream, and
subsequent writes are allowed. Explicit flushes can increase compressed size;
avoid flushing every small write when message latency is not required.

`ZInputStream` drains available decoded output before requesting more source
bytes. Modern `ReadAsync(Memory<byte>)` uses a ValueTask core; the older
Task-returning overload remains available.

For repeated independent messages, `ZOutputStream.Reset()` reuses the same codec
and output buffer after `Finish()` / `FinishAsync()`. It retains the sink and
flush mode, and resets totals and dictionary history. Reset rejects active,
failed, ended, or disposed streams. The caller must separate/frame independent
streams; this API does not enable automatic concatenated-member decoding.

```csharp
using var target = new MemoryStream();
using var compressor = new ZOutputStream(target, 6, leaveOpen: true);
compressor.Write(firstMessage, 0, firstMessage.Length);
compressor.Finish();
byte[] firstPacket = target.ToArray();

target.SetLength(0);
target.Position = 0;
compressor.Reset();
compressor.Write(secondMessage, 0, secondMessage.Length);
compressor.Finish();
byte[] secondPacket = target.ToArray();
```

Low-level callers can use `ZStream.deflateReset()` / `inflateReset()` to discard
a current stream and reuse codec storage. Set input/output fields for the new
stream. Unlike the wrapper, these low-level methods permit discarding unfinished
data.

Default-size compressors cache up to four bundles of their large internal
buffers process-wide (at most 1 MiB of array payload). All four arrays are
cleared before a bundle is returned to the cache. Cache misses allocate
normally, custom window sizes bypass the cache, and concurrent streams own
separate leases. This trades bounded retained memory for less allocation/GC
pressure; a cold or highly concurrent workload may still allocate full buffers.
Explicit Reset avoids even the per-instance allocation. Caller buffers and
disposed input sources are no longer retained by stream wrappers.

See [stream-memory
measurements](benchmarks/zlib.net.StreamMemory.Perf/README.md) for before/after
timings, allocations, and reproduction commands.

For a smaller active compressor, choose a memory level explicitly:

```csharp
using var compressor = new ZOutputStream(target, level: 6, leaveOpen: true,
    bufferSize: 16384, memoryLevel: 6);
```

Memory level 6 uses 160 KiB for the four main codec buffers, compared with 256
KiB at the default level 8. It keeps the same 32 KiB history window. This is an
opt-in memory/speed/ratio tradeoff: incompressible input can be substantially
slower, and compressed size can increase. Values 1 through 9 are supported;
custom memory levels bypass the default compressor buffer cache. Low-level
callers can use `deflateInit(level, bits, memoryLevel)`; negative `bits` still
selects raw DEFLATE.

Modern compression `Write(ReadOnlySpan<byte>)` feeds input directly into the
codec window, including stack-backed spans, without a pooled staging copy. The
Span decompression adapters still use bounded staging arrays. The codec's
resumable state stays on the heap.

Decoders cache at most one default 32 KiB history window and one lazily
allocated 17,280-byte Huffman table process-wide: 50,048 bytes of additional
cached array payload. Arrays are cleared before publication and exclusively
leased; custom history sizes bypass the window cache. This reduces warmed
allocations rather than the memory needed by simultaneously active decoders.
Both caches remain bounded, including under concurrent use.

See [algorithm experiments](benchmarks/zlib.net.Algorithm.Perf/README.md) for
the additional measurements, retained changes, and rejected prototypes,
including `stackalloc` and direct stored-block output.

## Building

```powershell
dotnet test
dotnet pack zlib.net.csproj -c Release
```

## Strong naming

All library targets are fully strong-name signed with the checked-in zlib.snk
key.
The public key token is d3655c1a090d0873. The test and benchmark friend
assemblies use the same key; no signing secret or platform-specific signing tool
is needed to build.

This establishes a stable assembly identity and supports consumers that require
signed references. It changes the identity of the previously unsigned assembly,
so rebuild consumers and check assembly-qualified names when upgrading. The
source-distribution key is public and is not a publisher-authentication
credential. Keep it stable across releases.
