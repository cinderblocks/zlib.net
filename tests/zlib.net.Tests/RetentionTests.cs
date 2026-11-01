using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ComponentAce.Compression.Libs.zlib;
using Xunit;
namespace zlib.net.Tests
{
    public class RetentionTests
    {
        private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference WriteLarge(ZOutputStream z) { var b = new byte[8 << 20]; z.Write(b, 0, b.Length); return new WeakReference(b); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference ReadLarge(ZInputStream z) { var b = new byte[8 << 20]; Assert.True(z.Read(b, 0, b.Length) > 0); return new WeakReference(b); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference DisposedSource(bool leaveOpen, out ZInputStream z)
        {
            var source = new MemoryStream(new byte[8 << 20]); z = new ZInputStream(source, leaveOpen); z.Dispose(); return new WeakReference(source);
        }
        [Fact]
        public void CompletedWritesDoNotRetainCallerInput()
        {
            using var z = new ZOutputStream(Stream.Null, 6, true);
            var weak = WriteLarge(z); Collect(); Assert.False(weak.IsAlive); GC.KeepAlive(z);
        }
        [Fact]
        public void CompletedReadsDoNotRetainCallerOutput()
        {
            using var z = new ZInputStream(new MemoryStream(CompletionTests.Pack(new byte[8 << 20])));
            var weak = ReadLarge(z); Collect(); Assert.False(weak.IsAlive); GC.KeepAlive(z);
        }
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DisposedReadersReleaseSourceReferences(bool leaveOpen)
        {
            var weak = DisposedSource(leaveOpen, out var z); Collect(); Assert.False(weak.IsAlive); GC.KeepAlive(z);
        }
#if NET8_0_OR_GREATER && !LIB_NETSTANDARD20
        [Fact]
        public async Task AsyncReadsAndWritesDetachCallerArrays()
        {
            var writer = new InspectOutput(); var input = new byte[100]; await writer.WriteAsync(input.AsMemory());
            Assert.False(writer.HasCallerInput); await writer.DisposeAsync();
            var reader = new InspectInput(CompletionTests.Pack(input)); await reader.ReadAsync(new byte[100].AsMemory());
            Assert.False(reader.HasCallerOutput); await reader.DisposeAsync();
        }
#endif
        private sealed class InspectOutput : ZOutputStream
        {
            public InspectOutput() : base(Stream.Null, 6, true) { }
            public bool HasCallerInput => z.next_in != null && z.next_in.Length != 0;
        }
        private sealed class InspectInput : ZInputStream
        {
            public InspectInput(byte[] data) : base(new MemoryStream(data)) { }
            public bool HasCallerOutput => z.next_out != null;
        }
    }
}
