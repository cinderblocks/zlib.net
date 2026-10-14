# zlib.net

[![CI](https://github.com/cinderblocks/zlib.net/actions/workflows/ci.yml/badge.svg)](https://github.com/cinderblocks/zlib.net/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/ZlibNet.svg)](https://www.nuget.org/packages/ZlibNet)

Managed version of the ZLIB compression library ported to .NET Core

This port is based on the 100% managed version of the library from [ComponentAce](http://www.componentace.com/zlib_.NET.htm).

## Installation

```
dotnet add package ZlibNet
```

Versions up to 1.1.0 were published under the misspelled ID `zlib.net-mutliplatform`. From 2.0.0 the package is `ZlibNet`.

Targets `netstandard2.0`, `netstandard2.1` and `net8.0`. The `Span`/`Memory` overloads and `DisposeAsync` are available on `netstandard2.1` and `net8.0`.

## Usage

```csharp
using ComponentAce.Compression.Libs.zlib;

// Compress
var compressed = new MemoryStream();
using (var zout = new ZOutputStream(compressed, zlibConst.Z_DEFAULT_COMPRESSION))
{
    zout.Write(data, 0, data.Length);
    zout.Finish();
    result = compressed.ToArray(); // ZOutputStream disposes the target stream, so read it before disposing
}

// Decompress
var decompressed = new MemoryStream();
using (var zout = new ZOutputStream(decompressed))
{
    zout.Write(compressedData, 0, compressedData.Length);
    zout.Finish();
    result = decompressed.ToArray();
}

// Async (netstandard2.1 / net8.0): DisposeAsync finishes the stream without blocking on the target
await using (var zout = new ZOutputStream(target, zlibConst.Z_DEFAULT_COMPRESSION, leaveOpen: true))
{
    await zout.WriteAsync(data);
}

await using (var zin = new ZInputStream(source))
{
    int read = await zin.ReadAsync(buffer.AsMemory());
}
```

## Migrating from 1.x

Switch the package reference to `ZlibNet`. The namespace and assembly name are unchanged. Breaking changes:

- `ZInputStream` is now a `Stream` rather than a `BinaryReader`. `Read(byte[], int, int)` returns decompressed data (previously it returned raw bytes
  from the underlying stream) and returns 0 at the end, like any other stream. `ReadByte()` replaces the no-argument `Read()`, and `ReadAsync` is supported.
  Compress mode (`new ZInputStream(stream, level)`) now works without setting `FlushMode`.
- Truncated compressed input now throws `ZStreamException` instead of silently ending the stream.
- `ZOutputStream.Read`, `Seek`, `SetLength`, `Length` and `Position` throw `NotSupportedException` instead of returning 0.
- `ZOutputStream.finish()`/`end()` and `ZInputStream.read()`/`skip()` are renamed `Finish()`/`End()`/`Read()`; the old names still work but are marked `[Obsolete]`.
- `SupportClass` and `Deflate` were never meant to be public and are now internal.
- New: both streams take a `leaveOpen` argument to keep the underlying stream open on dispose.

## Building

```
dotnet test
dotnet pack zlib.net.csproj -c Release
```
