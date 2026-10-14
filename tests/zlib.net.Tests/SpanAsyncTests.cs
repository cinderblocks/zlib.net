// The Span, Memory and async-dispose overloads exist only in the netstandard2.1 and net8.0 builds.
#if NET8_0_OR_GREATER && !LIB_NETSTANDARD20
using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using ComponentAce.Compression.Libs.zlib;
using Xunit;

namespace zlib.net.Tests
{
    public class SpanAsyncTests
    {
        private static byte[] Deflate(byte[] input, int level = 6)
        {
            var ms = new MemoryStream();
            using (var zout = new ZOutputStream(ms, level, leaveOpen: true))
                zout.Write(input, 0, input.Length);
            return ms.ToArray();
        }

        private static byte[] PlatformInflate(byte[] zlibData)
        {
            using var z = new ZLibStream(new MemoryStream(zlibData), CompressionMode.Decompress);
            using var dst = new MemoryStream();
            z.CopyTo(dst);
            return dst.ToArray();
        }

        private sealed class NonArrayMemory : MemoryManager<byte>
        {
            private readonly byte[] data;
            public NonArrayMemory(int length) { data = new byte[length]; }
            public byte[] Data => data;
            public override Span<byte> GetSpan() => data;
            public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
            public override void Unpin() { }
            protected override void Dispose(bool disposing) { }
            protected override bool TryGetArray(out ArraySegment<byte> segment) { segment = default; return false; }
        }

        /// <summary>Accepts only asynchronous use: any synchronous Write, Flush or Dispose fails the test.</summary>
        private sealed class AsyncOnlySink : Stream
        {
            public readonly MemoryStream Data = new MemoryStream();
            public bool DisposedAsync;
            public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("synchronous Write");
            public override void Flush() => throw new InvalidOperationException("synchronous Flush");
            protected override void Dispose(bool disposing)
            {
                if (disposing) throw new InvalidOperationException("synchronous Dispose");
            }
            public override Task WriteAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken ct)
                => Data.WriteAsync(buffer, offset, count, ct);
            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, System.Threading.CancellationToken ct = default)
                => Data.WriteAsync(buffer, ct);
            public override Task FlushAsync(System.Threading.CancellationToken ct) => Task.CompletedTask;
            public override ValueTask DisposeAsync() { DisposedAsync = true; return default; }
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }

        private sealed class TrackingSource : MemoryStream
        {
            public bool DisposedAsync;
            public bool DisposedSync;
            public TrackingSource(byte[] data) : base(data) { }
            public override ValueTask DisposeAsync() { DisposedAsync = true; return base.DisposeAsync(); }
            protected override void Dispose(bool disposing) { DisposedSync = true; base.Dispose(disposing); }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(100)]
        [InlineData(4096)]
        [InlineData(16 * 1024)]
        [InlineData(100_000)]
        public void ZInputStreamReadsIntoSpans(int spanSize)
        {
            var data = TestData.Mixed(300_000);
            using var zin = new ZInputStream(new MemoryStream(Deflate(data)));
            var result = new MemoryStream();
            var buffer = new byte[spanSize];
            int n;
            while ((n = zin.Read(buffer.AsSpan())) > 0)
                result.Write(buffer, 0, n);
            Assert.Equal(data, result.ToArray());
        }

        [Fact]
        public void ZInputStreamSpanReadHonoursTheSliceItIsGiven()
        {
            var data = TestData.Text(10_000);
            using var zin = new ZInputStream(new MemoryStream(Deflate(data)));
            var buffer = new byte[100];
            Array.Fill(buffer, (byte)0xEE);
            int n = zin.Read(buffer.AsSpan(10, 50));
            Assert.True(n > 0 && n <= 50);
            Assert.All(buffer.AsSpan(0, 10).ToArray(), b => Assert.Equal(0xEE, b));
            Assert.All(buffer.AsSpan(10 + n).ToArray(), b => Assert.Equal(0xEE, b));
            Assert.Equal(data.AsSpan(0, n).ToArray(), buffer.AsSpan(10, n).ToArray());
        }

        [Fact]
        public void ZInputStreamEmptySpanReadReturnsZero()
        {
            using var zin = new ZInputStream(new MemoryStream(Deflate(TestData.Text(100))));
            Assert.Equal(0, zin.Read(Span<byte>.Empty));
        }

        [Fact]
        public void ZInputStreamSpanReadInCompressMode()
        {
            var data = TestData.Text(250_000);
            using var zin = new ZInputStream(new MemoryStream(data), zlibConst.Z_DEFAULT_COMPRESSION);
            var compressed = new MemoryStream();
            var buffer = new byte[7000];
            int n;
            while ((n = zin.Read(buffer.AsSpan())) > 0)
                compressed.Write(buffer, 0, n);
            Assert.Equal(data, PlatformInflate(compressed.ToArray()));
        }

        [Fact]
        public async Task ZInputStreamReadsIntoMemory()
        {
            var data = TestData.Mixed(300_000);
            using var zin = new ZInputStream(new MemoryStream(Deflate(data)));
            var result = new MemoryStream();
            var buffer = new byte[20_000];
            int n;
            while ((n = await zin.ReadAsync(buffer.AsMemory())) > 0)
                result.Write(buffer, 0, n);
            Assert.Equal(data, result.ToArray());
        }

        [Fact]
        public async Task ZInputStreamReadsIntoMemoryThatIsNotAnArray()
        {
            var data = TestData.Mixed(100_000);
            using var zin = new ZInputStream(new MemoryStream(Deflate(data)));
            using var owner = new NonArrayMemory(40_000);
            var result = new MemoryStream();
            int n;
            while ((n = await zin.ReadAsync(owner.Memory)) > 0)
                result.Write(owner.Data, 0, n);
            Assert.Equal(data, result.ToArray());
        }

        [Fact]
        public async Task ZInputStreamCopyToAsyncWorks()
        {
            var data = TestData.Text(500_000);
            using var zin = new ZInputStream(new MemoryStream(Deflate(data)));
            var result = new MemoryStream();
            await zin.CopyToAsync(result);
            Assert.Equal(data, result.ToArray());
        }

        [Fact]
        public async Task ZInputStreamReadsAfterDisposeThrow()
        {
            var zin = new ZInputStream(new MemoryStream(Deflate(TestData.Text(100))));
            await zin.DisposeAsync();
            Assert.Throws<ObjectDisposedException>(() => zin.Read(new byte[10].AsSpan()));
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await zin.ReadAsync(new byte[10].AsMemory()));
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await zin.ReadAsync(new NonArrayMemory(10).Memory));
        }

        [Fact]
        public async Task ZInputStreamDisposeAsyncDisposesTheSourceAsynchronously()
        {
            var source = new TrackingSource(Deflate(TestData.Text(1000)));
            var zin = new ZInputStream(source);
            await zin.DisposeAsync();
            Assert.True(source.DisposedAsync);
            await zin.DisposeAsync(); // idempotent
            zin.Dispose();
        }

        [Fact]
        public async Task ZInputStreamDisposeAsyncHonoursLeaveOpen()
        {
            var source = new TrackingSource(Deflate(TestData.Text(1000)));
            await using (new ZInputStream(source, leaveOpen: true)) { }
            Assert.False(source.DisposedAsync);
            Assert.False(source.DisposedSync);
        }

        [Fact]
        public async Task ZOutputStreamDisposeAsyncFinishesWithoutBlockingTheSink()
        {
            var data = TestData.Mixed(200_000);
            var sink = new AsyncOnlySink();
            var zout = new ZOutputStream(sink, 6);
            await zout.WriteAsync(data.AsMemory());
            await zout.DisposeAsync();

            Assert.True(sink.DisposedAsync);
            Assert.Equal(data, PlatformInflate(sink.Data.ToArray()));
            await zout.DisposeAsync(); // idempotent
        }

        [Fact]
        public async Task ZOutputStreamDisposeAsyncHonoursLeaveOpen()
        {
            var sink = new AsyncOnlySink();
            await using (var zout = new ZOutputStream(sink, 6, leaveOpen: true))
                await zout.WriteAsync(TestData.Text(1000).AsMemory());
            Assert.False(sink.DisposedAsync);
            Assert.NotEmpty(sink.Data.ToArray());
        }

        [Fact]
        public async Task ZOutputStreamDecompressesViaDisposeAsync()
        {
            var data = TestData.Text(150_000);
            var sink = new AsyncOnlySink();
            await using (var zout = new ZOutputStream(sink, leaveOpen: true))
                await zout.WriteAsync(Deflate(data).AsMemory());
            Assert.Equal(data, sink.Data.ToArray());
        }

        [Fact]
        public async Task ZOutputStreamFlushAsyncAfterDisposeThrows()
        {
            var zout = new ZOutputStream(new MemoryStream(), 6);
            await zout.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => zout.FlushAsync());
        }

        [Fact]
        public async Task ZOutputStreamSyncDisposeStillWorksAfterAsyncUse()
        {
            var ms = new MemoryStream();
            var zout = new ZOutputStream(ms, 6, leaveOpen: true);
            await zout.WriteAsync(TestData.Text(2000).AsMemory());
            zout.Dispose();
            Assert.Equal(TestData.Text(2000), PlatformInflate(ms.ToArray()));
        }
    }
}
#endif
