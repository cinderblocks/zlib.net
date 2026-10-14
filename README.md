# zlib.net

[![CI](https://github.com/cinderblocks/zlib.net/actions/workflows/ci.yml/badge.svg)](https://github.com/cinderblocks/zlib.net/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/zlib.net-multiplatform.svg)](https://www.nuget.org/packages/zlib.net-multiplatform)

Managed version of the ZLIB compression library ported to .NET Core

This port is based on the 100% managed version of the library from [ComponentAce](http://www.componentace.com/zlib_.NET.htm).

## Installation

```
dotnet add package zlib.net-multiplatform
```

Versions up to 1.1.0 were published under the misspelled ID `zlib.net-mutliplatform`. From 2.0.0 the package is `zlib.net-multiplatform`.

Targets `netstandard2.0` and `netstandard2.1`.

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
```

## Migrating from 1.x

Switch the package reference to `zlib.net-multiplatform`. The namespace and assembly name are unchanged. Breaking changes:

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
