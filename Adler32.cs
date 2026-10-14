#nullable disable
// Copyright (c) 2006, ComponentAce
// http://www.componentace.com
// All rights reserved.

// Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution. 
// Neither the name of ComponentAce nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission. 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.



/*
Copyright (c) 2000,2001,2002,2003 ymnk, JCraft,Inc. All rights reserved.

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
FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL JCRAFT,
INC. OR ANY CONTRIBUTORS TO THIS SOFTWARE BE LIABLE FOR ANY DIRECT, INDIRECT,
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

namespace ComponentAce.Compression.Libs.zlib
{
	internal sealed class Adler32
	{
		
		// largest prime smaller than 65536
		private const uint BASE = 65521;
		// NMAX is the largest n such that 255n(n+1)/2 + (n+1)(BASE-1) <= 2^32-1
		private const int NMAX = 5552;
		
		internal static long adler32(long adler, byte[] buf, int index, int len)
		{
			if (buf == null)
			{
				return 1L;
			}
			if ((uint)index > (uint)buf.Length || (uint)len > (uint)(buf.Length - index))
				throw new ArgumentOutOfRangeException(len < 0 ? nameof(len) : nameof(index));

			uint s1 = (uint) adler & 0xffff;
			uint s2 = (uint) (adler >> 16) & 0xffff;

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
			if (len >= VectorThreshold && System.Numerics.Vector.IsHardwareAccelerated)
			{
				AdlerVector(buf, ref index, ref len, ref s1, ref s2);
				if (len == 0)
					return ((long) s2 << 16) | s1;
			}
#endif

			while (len > 0)
			{
				int k = len < NMAX ? len : NMAX;
				len -= k;
				int end = index + k;

				// Unrolled by 16; the local copy of the array reference lets the JIT hoist the loads.
				while (index <= end - 16)
				{
					var b = buf;
					s1 += b[index]; s2 += s1;
					s1 += b[index + 1]; s2 += s1;
					s1 += b[index + 2]; s2 += s1;
					s1 += b[index + 3]; s2 += s1;
					s1 += b[index + 4]; s2 += s1;
					s1 += b[index + 5]; s2 += s1;
					s1 += b[index + 6]; s2 += s1;
					s1 += b[index + 7]; s2 += s1;
					s1 += b[index + 8]; s2 += s1;
					s1 += b[index + 9]; s2 += s1;
					s1 += b[index + 10]; s2 += s1;
					s1 += b[index + 11]; s2 += s1;
					s1 += b[index + 12]; s2 += s1;
					s1 += b[index + 13]; s2 += s1;
					s1 += b[index + 14]; s2 += s1;
					s1 += b[index + 15]; s2 += s1;
					index += 16;
				}
				while (index < end)
				{
					s1 += buf[index++]; s2 += s1;
				}
				s1 %= BASE;
				s2 %= BASE;
			}
			return ((long) s2 << 16) | s1;
		}
		

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
		private const int VectorThreshold = 128;

		// Weights W, W-1, ..., 1 for the bytes of one vector, split into the four uint vectors that widening produces.
		private static readonly System.Numerics.Vector<uint>[] weights = CreateWeights();

		private static System.Numerics.Vector<uint>[] CreateWeights()
		{
			int width = System.Numerics.Vector<byte>.Count;
			int lanes = System.Numerics.Vector<uint>.Count;
			var result = new System.Numerics.Vector<uint>[4];
			var values = new uint[lanes];
			for (int part = 0; part < 4; part++)
			{
				for (int lane = 0; lane < lanes; lane++)
					values[lane] = (uint) (width - (part * lanes + lane));
				result[part] = new System.Numerics.Vector<uint>(values);
			}
			return result;
		}

		// Consumes whole vectors from the input, updating s1/s2 modulo BASE. Leaves any remainder (< one vector, plus
		// whatever is left of the final block) for the scalar loop.
		private static void AdlerVector(byte[] buf, ref int index, ref int len, ref uint s1, ref uint s2)
		{
			int width = System.Numerics.Vector<byte>.Count;
			var w0 = weights[0]; var w1 = weights[1]; var w2 = weights[2]; var w3 = weights[3];

			while (len >= width)
			{
				// Bytes in this block: a multiple of the vector width, no more than NMAX.
				int chunks = Math.Min(len, NMAX) / width;
				int bytes = chunks * width;

				var vs1 = System.Numerics.Vector<uint>.Zero; // per-lane byte sums
				var vsPrev = System.Numerics.Vector<uint>.Zero; // per-lane sum of vs1 before each chunk
				var vs2 = System.Numerics.Vector<uint>.Zero; // per-lane weighted byte sums

				var data = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, System.Numerics.Vector<byte>>(new ReadOnlySpan<byte>(buf, index, bytes));
				for (int c = 0; c < data.Length; c++)
				{
					vsPrev += vs1;
					System.Numerics.Vector.Widen(data[c], out var lo, out var hi);
					System.Numerics.Vector.Widen(lo, out var u0, out var u1);
					System.Numerics.Vector.Widen(hi, out var u2, out var u3);
					vs1 += u0 + u1 + u2 + u3;
					vs2 += u0 * w0 + u1 * w1 + u2 * w2 + u3 * w3;
				}

				uint sum1 = 0, sumPrev = 0, sum2 = 0;
				for (int lane = 0; lane < System.Numerics.Vector<uint>.Count; lane++)
				{
					sum1 += vs1[lane]; sumPrev += vsPrev[lane]; sum2 += vs2[lane];
				}

				// s2 gains (width * s1-before-each-chunk) + the weighted sums; all of it fits in 32 bits for <= NMAX bytes.
				s2 += (uint) width * ((uint) chunks * s1 + sumPrev) + sum2;
				s1 += sum1;
				s1 %= BASE;
				s2 %= BASE;
				index += bytes;
				len -= bytes;
			}
		}
#endif
	}
}