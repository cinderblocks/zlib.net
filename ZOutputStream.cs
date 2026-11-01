// Copyright (c) 2006, ComponentAce
// http://www.componentace.com
// All rights reserved.

// Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution. 
// Neither the name of ComponentAce nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission. 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.


/*
Copyright (c) 2001 Lapo Luchini.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice,
this list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright 
notice, this list of conditions and the following disclaimer in 
the documentation and/or other materials provided with the distribution.

3. The names of the authors may not be used to endorse or promote products
derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED ``AS IS'' AND ANY EXPRESSED OR IMPLIED WARRANTIES,
INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE AUTHORS
OR ANY CONTRIBUTORS TO THIS SOFTWARE BE LIABLE FOR ANY DIRECT, INDIRECT,
INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA,
OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE,
EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/
/*
* This program is based on zlib-1.1.3, so all credit should go authors
* Jean-loup Gailly(jloup@gzip.org) and Mark Adler(madler@alumni.caltech.edu)
* and contributors of zlib.
*/
using System;
using System.IO;
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
#endif
namespace ComponentAce.Compression.Libs.zlib
{

    /// <summary>
    /// A write-only stream that deflates (compresses) or inflates (decompresses) everything written to it and passes the
    /// result to an underlying stream. Dispose it, or call <see cref="Finish"/>, to write any remaining data.
    /// </summary>
    public class ZOutputStream : System.IO.Stream, IDisposable
    {
        private void InitBlock()
        {
            flush_Renamed_Field = zlibConst.Z_NO_FLUSH;
            buf = new byte[bufsize];
        }
        /// <summary>The flush mode used after each write, one of the <c>Z_*_FLUSH</c> values in <see cref="zlibConst"/>. Defaults to <see cref="zlibConst.Z_NO_FLUSH"/>.</summary>
        public virtual int FlushMode
        {
            get => (flush_Renamed_Field);

            set => flush_Renamed_Field = value;
        }
        /// <summary> Returns the total number of bytes input so far.</summary>
        public virtual long TotalIn => z.total_in;

        /// <summary> Returns the total number of bytes output so far.</summary>
        public virtual long TotalOut => z.total_out;

        /// <summary>The underlying zlib stream.</summary>
        protected internal ZStream z = new ZStream();
        /// <summary>The size of the output buffer.</summary>
        protected internal int bufsize = 16 * 1024;
        /// <summary>Backing field of <see cref="FlushMode"/>.</summary>
        protected internal int flush_Renamed_Field;
        /// <summary>Output buffers.</summary>
        protected internal byte[] buf = Array.Empty<byte>(), buf1 = new byte[1];
        /// <summary>true when deflating, false when inflating.</summary>
        protected internal bool compress;

        private System.IO.Stream? out_Renamed;
        private readonly bool leaveOpen;
        private bool finished;
        private bool failed;
        private byte[]? flushBuffer;

        /// <summary> Creates a stream that inflates everything written to it into <paramref name="out_Renamed"/>.</summary>
        public ZOutputStream(System.IO.Stream out_Renamed) : this(out_Renamed, false)
        {
        }

        /// <summary> Creates a stream that inflates everything written to it into <paramref name="out_Renamed"/>.</summary>
        /// <param name="out_Renamed">The stream that receives the inflated data.</param>
        /// <param name="leaveOpen">true to leave <paramref name="out_Renamed"/> open when this stream is disposed.</param>
        public ZOutputStream(System.IO.Stream out_Renamed, bool leaveOpen) : this(out_Renamed, leaveOpen, 16 * 1024)
        {
        }

        /// <summary>Creates a decompression stream with a positive output buffer size, in bytes.</summary>
        public ZOutputStream(System.IO.Stream out_Renamed, bool leaveOpen, int bufferSize)
        {
            if (bufferSize <= 0) throw new ArgumentOutOfRangeException(nameof(bufferSize));
            bufsize = bufferSize;
            InitBlock();
            this.out_Renamed = out_Renamed ?? throw new ArgumentNullException(nameof(out_Renamed));
            this.leaveOpen = leaveOpen;
            if (z.inflateInit() != zlibConst.Z_OK)
                throw new ZStreamException("Unable to initialize decompression.");
            compress = false;
        }

        /// <summary> Creates a stream that deflates everything written to it into <paramref name="out_Renamed"/>.</summary>
        public ZOutputStream(System.IO.Stream out_Renamed, int level) : this(out_Renamed, level, false)
        {
        }

        /// <summary> Creates a stream that deflates everything written to it into <paramref name="out_Renamed"/>.</summary>
        /// <param name="out_Renamed">The stream that receives the deflated data.</param>
        /// <param name="level">The compression level, 0 (none) to 9 (best), or <see cref="zlibConst.Z_DEFAULT_COMPRESSION"/>.</param>
        /// <param name="leaveOpen">true to leave <paramref name="out_Renamed"/> open when this stream is disposed.</param>
        public ZOutputStream(System.IO.Stream out_Renamed, int level, bool leaveOpen) : this(out_Renamed, level, leaveOpen, 16 * 1024)
        {
        }

        /// <summary>Creates a compression stream with a positive output buffer size, in bytes.</summary>
        public ZOutputStream(System.IO.Stream out_Renamed, int level, bool leaveOpen, int bufferSize)
            : this(out_Renamed, level, leaveOpen, bufferSize, 8)
        {
        }

        /// <summary>Creates a compression stream with a positive output buffer size and memory level (1-9).</summary>
        /// <remarks>The default memory level is 8. Lower levels use smaller hash and symbol buffers, which may
        /// affect speed and compression ratio. At level 6 the main codec buffers use 160 KiB rather than 256 KiB.
        /// The 32 KiB history size is unchanged. Custom memory levels bypass the default buffer cache.</remarks>
        public ZOutputStream(System.IO.Stream out_Renamed, int level, bool leaveOpen, int bufferSize, int memoryLevel)
        {
            if (bufferSize <= 0) throw new ArgumentOutOfRangeException(nameof(bufferSize));
            if (memoryLevel < 1 || memoryLevel > 9) throw new ArgumentOutOfRangeException(nameof(memoryLevel));
            bufsize = bufferSize;
            InitBlock();
            this.out_Renamed = out_Renamed ?? throw new ArgumentNullException(nameof(out_Renamed));
            this.leaveOpen = leaveOpen;
            if (level < -1 || level > 9) throw new ArgumentOutOfRangeException(nameof(level));
            if (z.deflateInit(level, 15, memoryLevel) != zlibConst.Z_OK)
                throw new ZStreamException("Unable to initialize compression.");
            compress = true;
        }

        // Callers that go on to use out_Renamed! rely on this check.
        private void ThrowIfDisposed()
        {
            if (z == null || out_Renamed == null)
                throw new ObjectDisposedException(GetType().Name);
        }

        private static void ValidateBuffer(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || count > buffer.Length - offset) throw new ArgumentOutOfRangeException(nameof(count));
        }

        private void ThrowIfFailed()
        {
            if (failed) throw new InvalidOperationException("The stream failed and cannot be reused.");
        }

        private void ThrowIfCompleted()
        {
            ThrowIfFailed();
            if (finished)
            {
                if (!compress) throw new ZStreamException("Trailing data after the compressed stream.");
                throw new InvalidOperationException("The compressed stream has already ended.");
            }
        }

        // One codec step, shared by sync and async callers. Every iteration must consume input,
        // produce output, or end the stream; otherwise a corrupt/truncated stream could spin forever.
        private int Step(int mode, bool finishing) => Step(mode, finishing, buf, bufsize);

        private int Step(int mode, bool finishing, byte[] output, int size)
            => Step(mode, finishing, output, size, new DeflateInput(z.next_in));

        private int Step(int mode, bool finishing, byte[] output, int size, DeflateInput input)
        {
            int before = z.avail_in;
            z.next_out = output;
            z.next_out_index = 0;
            z.avail_out = size;
            int status = compress ? z.dstate!.deflate(z, mode, input) : z.inflate(mode);
            int produced = size - z.avail_out;
            if (status == zlibConst.Z_STREAM_END)
            {
                finished = true;
                if (z.avail_in != 0)
                    throw new ZStreamException("Trailing data after the compressed stream.");
            }
            else if (status != zlibConst.Z_OK &&
                     !(status == zlibConst.Z_BUF_ERROR && !finishing && z.avail_in == 0 && produced == 0))
                throw new ZStreamException((compress ? "de" : "in") + "flating: " + z.msg);

            if (!finished && before == z.avail_in && produced == 0 && (finishing || z.avail_in != 0))
                throw new ZStreamException("The compression engine made no progress.");
            return produced;
        }

        /// <summary>Writes a single byte.</summary>
        public void WriteByte(int b)
        {
            buf1[0] = (byte)b;
            Write(buf1, 0, 1);
        }
        //UPGRADE_TODO: The differences in the Expected value  of parameters for method 'WriteByte'  may cause compilation errors.  'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1092_3"'
        /// <inheritdoc />
        public override void WriteByte(byte b)
        {
            WriteByte(b);
        }

        /// <inheritdoc />
        public override void Write(Byte[] b1, int off, int len)
        {
            ThrowIfDisposed();
            ValidateBuffer(b1, off, len);
            if (len == 0)
                return;
            ThrowIfCompleted();
            z.next_in = b1;
            z.next_in_index = off;
            z.avail_in = len;
            try
            {
                do
                {
                    int produced = Step(flush_Renamed_Field, false);
                    if (produced > 0) out_Renamed!.Write(buf, 0, produced);
                }
                while (!finished && (z.avail_in > 0 || z.avail_out == 0));
            }
            catch { failed = true; throw; }
            finally
            {
                // Writes either consume all input or fail permanently. Never retain a caller's array
                // (or a pooled Span/Memory staging array) after the operation has completed.
                z.next_in = Array.Empty<byte>();
                z.next_in_index = z.avail_in = 0;
                z.next_out = null;
            }
        }

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
        // Large inputs are fed through a bounded pooled buffer instead of renting one as big as the input.
        private const int SpanChunkSize = 16 * 1024;

        /// <inheritdoc />
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ThrowIfDisposed();
            if (buffer.Length == 0)
                return;

            if (compress)
            {
                ThrowIfCompleted();
                z.next_in = Array.Empty<byte>();
                z.next_in_index = 0;
                z.avail_in = buffer.Length;
                var input = new DeflateInput(buffer);
                try
                {
                    do
                    {
                        int produced = Step(flush_Renamed_Field, false, buf, bufsize, input);
                        if (produced > 0) out_Renamed!.Write(buf, 0, produced);
                    }
                    while (!finished && (z.avail_in > 0 || z.avail_out == 0));
                }
                catch { failed = true; throw; }
                finally
                {
                    z.next_in_index = z.avail_in = 0;
                    z.next_out = null;
                }
                return;
            }

            byte[] rentedArray = ArrayPool<byte>.Shared.Rent(Math.Min(buffer.Length, SpanChunkSize));
            try
            {
                while (buffer.Length > 0)
                {
                    int count = Math.Min(buffer.Length, SpanChunkSize);
                    buffer.Slice(0, count).CopyTo(rentedArray);
                    Write(rentedArray, 0, count);
                    buffer = buffer.Slice(count);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentedArray);
            }
        }

        /// <summary>Writes the bytes in <paramref name="buffer"/>.</summary>
        public void Write(Span<byte> buffer)
        {
            Write((ReadOnlySpan<byte>)buffer);
        }

        /// <inheritdoc />
        public override async System.Threading.Tasks.Task WriteAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            ValidateBuffer(buffer, offset, count);
            cancellationToken.ThrowIfCancellationRequested();
            if (count == 0)
                return;

            ThrowIfCompleted();
            z.next_in = buffer;
            z.next_in_index = offset;
            z.avail_in = count;
            try
            {
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int produced = Step(flush_Renamed_Field, false);
                    if (produced > 0)
                        await out_Renamed!.WriteAsync(buf, 0, produced, cancellationToken).ConfigureAwait(false);
                }
                while (!finished && (z.avail_in > 0 || z.avail_out == 0));
            }
            catch { failed = true; throw; }
            finally
            {
                // Writes either consume all input or fail permanently. Never retain a caller's array
                // (or a pooled Span/Memory staging array) after the operation has completed.
                z.next_in = Array.Empty<byte>();
                z.next_in_index = z.avail_in = 0;
                z.next_out = null;
            }
        }

        /// <inheritdoc />
        public override async System.Threading.Tasks.ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, System.Threading.CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer.Length == 0)
                return;

            if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> segment))
            {
                await WriteAsync(segment.Array!, segment.Offset, segment.Count, cancellationToken).ConfigureAwait(false);
                return;
            }

            byte[] rentedArray = ArrayPool<byte>.Shared.Rent(Math.Min(buffer.Length, SpanChunkSize));
            try
            {
                while (buffer.Length > 0)
                {
                    int count = Math.Min(buffer.Length, SpanChunkSize);
                    buffer.Slice(0, count).CopyTo(rentedArray);
                    await WriteAsync(rentedArray, 0, count, cancellationToken).ConfigureAwait(false);
                    buffer = buffer.Slice(count);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentedArray);
            }
        }

        /// <inheritdoc />
        public override async System.Threading.Tasks.Task FlushAsync(System.Threading.CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfFailed();
            try
            {
                if (compress && !finished)
                {
                    byte[] output = GetFlushBuffer();
                    do
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int produced = Step(zlibConst.Z_SYNC_FLUSH, false, output, output.Length);
                        if (produced > 0)
                            await out_Renamed!.WriteAsync(output, 0, produced, cancellationToken).ConfigureAwait(false);
                    }
                    while (z.avail_out == 0);
                }
                await out_Renamed!.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch { failed = true; throw; }
        }

        /// <summary>Asynchronously writes any remaining data to the underlying stream and finishes the compressed or decompressed stream.</summary>
        public async System.Threading.Tasks.ValueTask FinishAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfFailed();
            try
            {
                while (!finished)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int produced = Step(zlibConst.Z_FINISH, true);
                    if (produced > 0)
                        await out_Renamed!.WriteAsync(new ReadOnlyMemory<byte>(buf, 0, produced), cancellationToken).ConfigureAwait(false);
                }
                await FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch { failed = true; throw; }
        }

        /// <summary>
        /// Asynchronously finishes the stream and releases it, like <see cref="Dispose(bool)"/> but without blocking on the
        /// underlying stream. Finalization and output errors are propagated after releasing the codec state.
        /// </summary>
        public override async System.Threading.Tasks.ValueTask DisposeAsync()
        {
            var output = out_Renamed;
            if (output == null)
                return;

            try
            {
                if (!failed && z != null) await FinishAsync().ConfigureAwait(false);
            }
            finally
            {
                End();
                out_Renamed = null;
                if (!leaveOpen)
                    await output.DisposeAsync().ConfigureAwait(false);
            }
        }
#endif

        /// <summary> Completes the compressed (or decompressed) stream and flushes the underlying stream.</summary>
        public virtual void Finish()
        {
            ThrowIfDisposed();
            ThrowIfFailed();
            try
            {
                while (!finished)
                {
                    int produced = Step(zlibConst.Z_FINISH, true);
                    if (produced > 0) out_Renamed!.Write(buf, 0, produced);
                }
                Flush();
            }
            catch { failed = true; throw; }
        }
        /// <summary>Obsolete spelling of <see cref="Finish"/>.</summary>
        [Obsolete("Use Finish().")]
        public virtual void finish() => Finish();

        /// <summary>Starts another independent zlib stream on the same sink, reusing codec buffers.</summary>
        /// <remarks>Call Finish first. The sink remains open, flush mode is preserved, and counters and dictionary history reset.
        /// The caller must frame or separate the resulting streams; decompression does not accept concatenated members automatically.</remarks>
        public void Reset()
        {
            ThrowIfDisposed();
            ThrowIfFailed();
            if (!finished) throw new InvalidOperationException("Finish the current stream before resetting it.");
            int status = compress ? z.deflateReset() : z.inflateReset();
            if (status != zlibConst.Z_OK) throw new ZStreamException("Unable to reset the codec.");
            z.next_in = Array.Empty<byte>();
            z.next_out = null;
            z.next_in_index = z.avail_in = z.next_out_index = z.avail_out = 0;
            finished = false;
        }

        /// <summary> Releases the native-style zlib state. Called automatically on dispose.</summary>
        public virtual void End()
        {
            if (z == null)
                return;

            if (compress)
            {
                z.deflateEnd();
            }
            else
            {
                z.inflateEnd();
            }
            z.free();
            buf = Array.Empty<byte>();
            flushBuffer = null;
            z = null!; // the stream is unusable from here on; every public member checks for disposal first
        }

        /// <summary>Obsolete spelling of <see cref="End"/>.</summary>
        [Obsolete("Use End().")]
        public virtual void end() => End();

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing && out_Renamed != null)
            {
                try
                {
                    if (!failed && z != null) Finish();
                }
                finally
                {
                    var output = out_Renamed;
                    End();
                    out_Renamed = null;
                    if (!leaveOpen)
                        output.Dispose();
                }
            }
        }

        /// <inheritdoc />
        public override void Flush()
        {
            ThrowIfDisposed();
            ThrowIfFailed();
            try
            {
                if (compress && !finished)
                {
                    byte[] output = GetFlushBuffer();
                    do
                    {
                        int produced = Step(zlibConst.Z_SYNC_FLUSH, false, output, output.Length);
                        if (produced > 0) out_Renamed!.Write(output, 0, produced);
                    }
                    while (z.avail_out == 0);
                }
                out_Renamed!.Flush();
            }
            catch { failed = true; throw; }
        }

        // A sync-flush marker needs room to finish without repeatedly emitting empty markers.
        // The normal output buffer may be as small as one byte through the public constructor.
        private byte[] GetFlushBuffer() => bufsize >= 8 ? buf : (flushBuffer ??= new byte[8]);

        /// <inheritdoc />
        public override bool CanRead => false;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => out_Renamed != null && z != null && !finished && !failed && out_Renamed.CanWrite;

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
    }
}
