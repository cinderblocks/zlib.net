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

    public class ZOutputStream : System.IO.Stream, IDisposable
    {
        private void InitBlock()
        {
            flush_Renamed_Field = zlibConst.Z_NO_FLUSH;
            buf = new byte[bufsize];
        }
        public virtual int FlushMode
        {
            get => (flush_Renamed_Field);

            set => flush_Renamed_Field = value;
        }
        /// <summary> Returns the total number of bytes input so far.</summary>
        public virtual long TotalIn => z.total_in;

        /// <summary> Returns the total number of bytes output so far.</summary>
        public virtual long TotalOut => z.total_out;

        protected internal ZStream z = new ZStream();
        protected internal int bufsize = 4096;
        protected internal int flush_Renamed_Field;
        protected internal byte[] buf, buf1 = new byte[1];
        protected internal bool compress;

        private System.IO.Stream out_Renamed;
        private readonly bool leaveOpen;

        /// <summary> Creates a stream that inflates everything written to it into <paramref name="out_Renamed"/>.</summary>
        public ZOutputStream(System.IO.Stream out_Renamed) : this(out_Renamed, false)
        {
        }

        /// <summary> Creates a stream that inflates everything written to it into <paramref name="out_Renamed"/>.</summary>
        /// <param name="leaveOpen">true to leave <paramref name="out_Renamed"/> open when this stream is disposed.</param>
        public ZOutputStream(System.IO.Stream out_Renamed, bool leaveOpen)
        {
            InitBlock();
            this.out_Renamed = out_Renamed ?? throw new ArgumentNullException(nameof(out_Renamed));
            this.leaveOpen = leaveOpen;
            z.inflateInit();
            compress = false;
        }

        /// <summary> Creates a stream that deflates everything written to it into <paramref name="out_Renamed"/>.</summary>
        public ZOutputStream(System.IO.Stream out_Renamed, int level) : this(out_Renamed, level, false)
        {
        }

        /// <summary> Creates a stream that deflates everything written to it into <paramref name="out_Renamed"/>.</summary>
        /// <param name="leaveOpen">true to leave <paramref name="out_Renamed"/> open when this stream is disposed.</param>
        public ZOutputStream(System.IO.Stream out_Renamed, int level, bool leaveOpen)
        {
            InitBlock();
            this.out_Renamed = out_Renamed ?? throw new ArgumentNullException(nameof(out_Renamed));
            this.leaveOpen = leaveOpen;
            z.deflateInit(level);
            compress = true;
        }

        private void ThrowIfDisposed()
        {
            if (z == null || out_Renamed == null)
                throw new ObjectDisposedException(GetType().Name);
        }

        public void WriteByte(int b)
        {
            buf1[0] = (byte)b;
            Write(buf1, 0, 1);
        }
        //UPGRADE_TODO: The differences in the Expected value  of parameters for method 'WriteByte'  may cause compilation errors.  'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1092_3"'
        public override void WriteByte(byte b)
        {
            WriteByte(b);
        }

        public override void Write(Byte[] b1, int off, int len)
        {
            ThrowIfDisposed();
            if (len == 0)
                return;
            int err;
            z.next_in = b1;
            z.next_in_index = off;
            z.avail_in = len;
            do
            {
                z.next_out = buf;
                z.next_out_index = 0;
                z.avail_out = bufsize;
                err = compress ? z.deflate(flush_Renamed_Field) : z.inflate(flush_Renamed_Field);
                if (err != zlibConst.Z_OK && err != zlibConst.Z_STREAM_END)
                    throw new ZStreamException((compress ? "de" : "in") + "flating: " + z.msg);
                out_Renamed.Write(buf, 0, bufsize - z.avail_out);
            }
            while (z.avail_in > 0 || z.avail_out == 0);
        }

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
        // Large inputs are fed through a bounded pooled buffer instead of renting one as big as the input.
        private const int SpanChunkSize = 16 * 1024;

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ThrowIfDisposed();
            if (buffer.Length == 0)
                return;

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

        public void Write(Span<byte> buffer)
        {
            Write((ReadOnlySpan<byte>)buffer);
        }

        public override async System.Threading.Tasks.Task WriteAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (count == 0)
                return;

            z.next_in = buffer;
            z.next_in_index = offset;
            z.avail_in = count;

            int err;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                z.next_out = buf;
                z.next_out_index = 0;
                z.avail_out = bufsize;
                err = compress ? z.deflate(flush_Renamed_Field) : z.inflate(flush_Renamed_Field);
                if (err != zlibConst.Z_OK && err != zlibConst.Z_STREAM_END)
                    throw new ZStreamException((compress ? "de" : "in") + "flating: " + z.msg);
                await out_Renamed.WriteAsync(buf, 0, bufsize - z.avail_out, cancellationToken).ConfigureAwait(false);
            }
            while (z.avail_in > 0 || z.avail_out == 0);
        }

        public override async System.Threading.Tasks.ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, System.Threading.CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (buffer.Length == 0)
                return;

            if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> segment))
            {
                await WriteAsync(segment.Array, segment.Offset, segment.Count, cancellationToken).ConfigureAwait(false);
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

        public override async System.Threading.Tasks.Task FlushAsync(System.Threading.CancellationToken cancellationToken)
        {
            await out_Renamed.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public async System.Threading.Tasks.ValueTask FinishAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                z.next_out = buf;
                z.next_out_index = 0;
                z.avail_out = bufsize;
                int err = compress ? z.deflate(zlibConst.Z_FINISH) : z.inflate(zlibConst.Z_FINISH);
                if (err != zlibConst.Z_STREAM_END && err != zlibConst.Z_OK)
                    throw new ZStreamException((compress ? "de" : "in") + "flating: " + z.msg);
                if (bufsize - z.avail_out > 0)
                {
                    await out_Renamed.WriteAsync(new ReadOnlyMemory<byte>(buf, 0, bufsize - z.avail_out), cancellationToken).ConfigureAwait(false);
                }
            }
            while (z.avail_in > 0 || z.avail_out == 0);

            try
            {
                await FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }
#endif

        /// <summary> Completes the compressed (or decompressed) stream and flushes the underlying stream.</summary>
        public virtual void Finish()
        {
            ThrowIfDisposed();
            do
            {
                z.next_out = buf;
                z.next_out_index = 0;
                z.avail_out = bufsize;
                int err = compress ? z.deflate(zlibConst.Z_FINISH) : z.inflate(zlibConst.Z_FINISH);
                if (err != zlibConst.Z_STREAM_END && err != zlibConst.Z_OK)
                    throw new ZStreamException((compress ? "de" : "in") + "flating: " + z.msg);
                if (bufsize - z.avail_out > 0)
                {
                    out_Renamed.Write(buf, 0, bufsize - z.avail_out);
                }
            }
            while (z.avail_in > 0 || z.avail_out == 0);
            try
            {
                Flush();
            }
            catch
            {
            }
        }
        [Obsolete("Use Finish().")]
        public virtual void finish() => Finish();

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
            z = null;
        }

        [Obsolete("Use End().")]
        public virtual void end() => End();

        protected override void Dispose(bool disposing)
        {
            if (disposing && out_Renamed != null)
            {
                try
                {
                    Finish();
                }
                catch
                {
                }
                finally
                {
                    End();
                    if (!leaveOpen)
                        out_Renamed.Dispose();
                    out_Renamed = null;
                }
            }
        }

        public override void Flush()
        {
            ThrowIfDisposed();
            out_Renamed.Flush();
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => out_Renamed != null;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
    }
}
