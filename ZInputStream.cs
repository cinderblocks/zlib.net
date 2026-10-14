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
using System.Threading;
using System.Threading.Tasks;

namespace ComponentAce.Compression.Libs.zlib
{
	/// <summary>
	/// A read-only stream that inflates (default) or deflates the data of an underlying stream as it is read.
	/// </summary>
	public class ZInputStream : Stream
	{
		/// <summary> The flush mode passed to inflate/deflate. In compress mode the stream is finished automatically once the input is exhausted.</summary>
		public virtual int FlushMode
		{
			get => flush;
			set => flush = value;
		}

		/// <summary> Returns the total number of bytes input so far.</summary>
		public virtual long TotalIn => z.total_in;

		/// <summary> Returns the total number of bytes output so far.</summary>
		public virtual long TotalOut => z.total_out;

		protected ZStream z = new ZStream();
		protected int bufsize = 512;
		protected int flush = zlibConst.Z_NO_FLUSH;
		protected byte[] buf, buf1 = new byte[1];
		protected bool compress;

		internal Stream in_Renamed;
		private bool leaveOpen;
		private bool nomoreinput;
		private bool finished;
		private bool disposed;

		/// <summary> Creates a stream that inflates the data read from <paramref name="in_Renamed"/>.</summary>
		public ZInputStream(Stream in_Renamed) : this(in_Renamed, false)
		{
		}

		/// <summary> Creates a stream that inflates the data read from <paramref name="in_Renamed"/>.</summary>
		public ZInputStream(Stream in_Renamed, bool leaveOpen)
		{
			Init(in_Renamed, leaveOpen);
			z.inflateInit();
			compress = false;
		}

		/// <summary> Creates a stream that deflates the data read from <paramref name="in_Renamed"/>.</summary>
		public ZInputStream(Stream in_Renamed, int level) : this(in_Renamed, level, false)
		{
		}

		/// <summary> Creates a stream that deflates the data read from <paramref name="in_Renamed"/>.</summary>
		public ZInputStream(Stream in_Renamed, int level, bool leaveOpen)
		{
			Init(in_Renamed, leaveOpen);
			z.deflateInit(level);
			compress = true;
		}

		private void Init(Stream source, bool leaveOpen)
		{
			in_Renamed = source ?? throw new ArgumentNullException(nameof(source));
			this.leaveOpen = leaveOpen;
			buf = new byte[bufsize];
			z.next_in = buf;
			z.next_in_index = 0;
			z.avail_in = 0;
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			Prepare(buffer, offset, count);
			if (count == 0 || finished)
				return 0;

			do
			{
				if (z.avail_in == 0 && !nomoreinput)
					SetInput(in_Renamed.Read(buf, 0, bufsize));
			}
			while (Step(count));
			return count - z.avail_out;
		}

		public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			Prepare(buffer, offset, count);
			if (count == 0 || finished)
				return 0;

			do
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (z.avail_in == 0 && !nomoreinput)
					SetInput(await in_Renamed.ReadAsync(buf, 0, bufsize, cancellationToken).ConfigureAwait(false));
			}
			while (Step(count));
			return count - z.avail_out;
		}

		private void Prepare(byte[] buffer, int offset, int count)
		{
			if (buffer == null) throw new ArgumentNullException(nameof(buffer));
			if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
			if (count < 0 || count > buffer.Length - offset) throw new ArgumentOutOfRangeException(nameof(count));
			if (disposed) throw new ObjectDisposedException(GetType().Name);

			z.next_out = buffer;
			z.next_out_index = offset;
			z.avail_out = count;
		}

		private void SetInput(int bytesRead)
		{
			z.next_in_index = 0;
			z.avail_in = bytesRead > 0 ? bytesRead : 0;
			if (bytesRead <= 0)
				nomoreinput = true;
		}

		/// <summary> Runs one inflate/deflate step. Returns true if more input is needed to produce any output.</summary>
		private bool Step(int count)
		{
			// Once the input is exhausted a compressor must be told to finish, otherwise it never emits its trailer.
			int mode = compress && nomoreinput && z.avail_in == 0 ? zlibConst.Z_FINISH : flush;
			int err = compress ? z.deflate(mode) : z.inflate(mode);

			if (err == zlibConst.Z_STREAM_END)
			{
				finished = true;
				return false;
			}
			if (err == zlibConst.Z_BUF_ERROR && !compress && nomoreinput && z.avail_in == 0)
			{
				if (z.avail_out == count)
					throw new ZStreamException("inflating: unexpected end of compressed data");
				return false;
			}
			if (err != zlibConst.Z_OK)
				throw new ZStreamException((compress ? "de" : "in") + "flating: " + z.msg);

			return z.avail_out == count;
		}

		/// <summary> Reads the next decompressed (or compressed) byte, or -1 at the end of the stream.</summary>
		public override int ReadByte()
		{
			return Read(buf1, 0, 1) == 0 ? -1 : buf1[0];
		}

		/// <summary> Use <see cref="Read(byte[], int, int)"/>; this returns -1 instead of 0 at the end of the stream.</summary>
		[Obsolete("Use Read(byte[], int, int), which returns 0 at the end of the stream.")]
		public int read(byte[] b, int off, int len)
		{
			int n = Read(b, off, len);
			return n == 0 && len > 0 ? -1 : n;
		}

		/// <summary> Reads and discards up to <paramref name="n"/> bytes, returning the number skipped.</summary>
		[Obsolete("Read from the stream and discard the data instead.")]
		public long skip(long n)
		{
			var tmp = new byte[(int)Math.Min(n, 512)];
			long skipped = 0;
			while (skipped < n)
			{
				int read = Read(tmp, 0, (int)Math.Min(tmp.Length, n - skipped));
				if (read == 0)
					break;
				skipped += read;
			}
			return skipped;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && !disposed)
			{
				disposed = true;
				if (compress)
					z.deflateEnd();
				else
					z.inflateEnd();
				z.free();

				if (!leaveOpen)
					in_Renamed.Dispose();
			}
			base.Dispose(disposing);
		}

		public override void Flush()
		{
		}

		public override bool CanRead => !disposed;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}
}
