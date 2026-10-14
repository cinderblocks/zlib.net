# zlib.net

[![CI](https://github.com/cinderblocks/zlib.net/actions/workflows/ci.yml/badge.svg)](https://github.com/cinderblocks/zlib.net/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/zlib.net-mutliplatform.svg)](https://www.nuget.org/packages/zlib.net-mutliplatform)

Managed version of the ZLIB compression library ported to .NET Core

This port is based on the 100% managed version of the library from [ComponentAce](http://www.componentace.com/zlib_.NET.htm).

## Installation

```
dotnet add package zlib.net-mutliplatform
```

(The package ID really is spelled `mutliplatform`; it is kept for compatibility with existing consumers.)

Targets `netstandard2.0` and `netstandard2.1`.

## Usage

```csharp
using ComponentAce.Compression.Libs.zlib;

// Compress
var compressed = new MemoryStream();
using (var zout = new ZOutputStream(compressed, zlibConst.Z_DEFAULT_COMPRESSION))
{
    zout.Write(data, 0, data.Length);
    zout.finish();
    result = compressed.ToArray(); // ZOutputStream disposes the target stream, so read it before disposing
}

// Decompress
var decompressed = new MemoryStream();
using (var zout = new ZOutputStream(decompressed))
{
    zout.Write(compressedData, 0, compressedData.Length);
    zout.finish();
    result = decompressed.ToArray();
}
```

## Building

```
dotnet test
dotnet pack zlib.net.csproj -c Release
```

`dotnet pack` validates the package against the last published release and fails on API or target-framework regressions.

## Releasing

Push a version tag (e.g. `1.1.1`). GitHub Actions runs the tests, packs with the tag as the version, publishes to nuget.org
(requires the `NUGET_API_KEY` repository secret) and creates a GitHub release.
