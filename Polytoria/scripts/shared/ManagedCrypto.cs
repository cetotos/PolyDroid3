// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Polytoria.Shared;

// manual sha256 md5 and hmac because libssl crashes on android and i do NOT know how to fix it
internal static class ManagedCrypto
{
	public static byte[] Sha256(ReadOnlySpan<byte> data)
	{
		Span<uint> H = stackalloc uint[8]
		{
			0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a,
			0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19,
		};

		int msgLen = data.Length;
		long bitLen = (long)msgLen * 8;
		int padLen = (msgLen + 9 + 63) / 64 * 64;
		byte[] buf = new byte[padLen];
		data.CopyTo(buf);
		buf[msgLen] = 0x80;
		for (int i = 0; i < 8; i++)
		{
			buf[padLen - 8 + i] = (byte)(bitLen >> ((7 - i) * 8));
		}

		Span<uint> W = stackalloc uint[64];
		for (int blk = 0; blk < padLen; blk += 64)
		{
			for (int i = 0; i < 16; i++)
			{
				W[i] = ((uint)buf[blk + i * 4] << 24)
					 | ((uint)buf[blk + i * 4 + 1] << 16)
					 | ((uint)buf[blk + i * 4 + 2] << 8)
					 | buf[blk + i * 4 + 3];
			}
			for (int i = 16; i < 64; i++)
			{
				uint s0 = RotR(W[i - 15], 7) ^ RotR(W[i - 15], 18) ^ (W[i - 15] >> 3);
				uint s1 = RotR(W[i - 2], 17) ^ RotR(W[i - 2], 19) ^ (W[i - 2] >> 10);
				W[i] = W[i - 16] + s0 + W[i - 7] + s1;
			}

			uint a = H[0], b = H[1], c = H[2], d = H[3];
			uint e = H[4], f = H[5], g = H[6], h = H[7];

			for (int i = 0; i < 64; i++)
			{
				uint S1 = RotR(e, 6) ^ RotR(e, 11) ^ RotR(e, 25);
				uint ch = (e & f) ^ (~e & g);
				uint t1 = h + S1 + ch + K256[i] + W[i];
				uint S0 = RotR(a, 2) ^ RotR(a, 13) ^ RotR(a, 22);
				uint mj = (a & b) ^ (a & c) ^ (b & c);
				uint t2 = S0 + mj;
				h = g; g = f; f = e;
				e = d + t1;
				d = c; c = b; b = a;
				a = t1 + t2;
			}

			H[0] += a; H[1] += b; H[2] += c; H[3] += d;
			H[4] += e; H[5] += f; H[6] += g; H[7] += h;
		}

		byte[] outBuf = new byte[32];
		for (int i = 0; i < 8; i++)
		{
			outBuf[i * 4]     = (byte)(H[i] >> 24);
			outBuf[i * 4 + 1] = (byte)(H[i] >> 16);
			outBuf[i * 4 + 2] = (byte)(H[i] >> 8);
			outBuf[i * 4 + 3] = (byte)H[i];
		}
		return outBuf;
	}

	public static byte[] Md5(ReadOnlySpan<byte> data)
	{
		uint a0 = 0x67452301, b0 = 0xefcdab89, c0 = 0x98badcfe, d0 = 0x10325476;

		int msgLen = data.Length;
		long bitLen = (long)msgLen * 8;
		int padLen = (msgLen + 9 + 63) / 64 * 64;
		byte[] buf = new byte[padLen];
		data.CopyTo(buf);
		buf[msgLen] = 0x80;
		for (int i = 0; i < 8; i++)
		{
			buf[padLen - 8 + i] = (byte)(bitLen >> (i * 8));
		}

		Span<uint> M = stackalloc uint[16];
		for (int blk = 0; blk < padLen; blk += 64)
		{
			for (int i = 0; i < 16; i++)
			{
				M[i] = buf[blk + i * 4]
					 | ((uint)buf[blk + i * 4 + 1] << 8)
					 | ((uint)buf[blk + i * 4 + 2] << 16)
					 | ((uint)buf[blk + i * 4 + 3] << 24);
			}

			uint A = a0, B = b0, C = c0, D = d0;
			for (int i = 0; i < 64; i++)
			{
				uint F;
				int g;
				if (i < 16)      { F = (B & C) | (~B & D); g = i; }
				else if (i < 32) { F = (D & B) | (~D & C); g = (5 * i + 1) % 16; }
				else if (i < 48) { F = B ^ C ^ D;          g = (3 * i + 5) % 16; }
				else             { F = C ^ (B | ~D);       g = (7 * i) % 16; }

				uint temp = D;
				D = C;
				C = B;
				B = B + RotL(A + F + Md5T[i] + M[g], Md5R[i]);
				A = temp;
			}

			a0 += A; b0 += B; c0 += C; d0 += D;
		}

		byte[] outBuf = new byte[16];
		WriteLE(outBuf, 0,  a0);
		WriteLE(outBuf, 4,  b0);
		WriteLE(outBuf, 8,  c0);
		WriteLE(outBuf, 12, d0);
		return outBuf;
	}

	public static byte[] HmacMd5(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
	{
		const int blockSize = 64;
		Span<byte> k = stackalloc byte[blockSize];
		if (key.Length > blockSize)
		{
			Md5(key).CopyTo(k);
		}
		else
		{
			key.CopyTo(k);
		}

		Span<byte> ipadKey = stackalloc byte[blockSize];
		Span<byte> opadKey = stackalloc byte[blockSize];
		for (int i = 0; i < blockSize; i++)
		{
			ipadKey[i] = (byte)(k[i] ^ 0x36);
			opadKey[i] = (byte)(k[i] ^ 0x5c);
		}

		byte[] inner = new byte[blockSize + data.Length];
		ipadKey.CopyTo(inner);
		data.CopyTo(inner.AsSpan(blockSize));
		byte[] innerHash = Md5(inner);

		byte[] outer = new byte[blockSize + 16];
		opadKey.CopyTo(outer);
		innerHash.CopyTo(outer.AsSpan(blockSize));
		return Md5(outer);
	}

	private static uint RotR(uint x, int n) => (x >> n) | (x << (32 - n));
	private static uint RotL(uint x, int n) => (x << n) | (x >> (32 - n));

	private static void WriteLE(byte[] dst, int off, uint v)
	{
		dst[off]     = (byte)v;
		dst[off + 1] = (byte)(v >> 8);
		dst[off + 2] = (byte)(v >> 16);
		dst[off + 3] = (byte)(v >> 24);
	}

	private static readonly uint[] K256 =
	{
		0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
		0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
		0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
		0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
		0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
		0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
		0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
		0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
	};

	private static readonly uint[] Md5T =
	{
		0xd76aa478, 0xe8c7b756, 0x242070db, 0xc1bdceee, 0xf57c0faf, 0x4787c62a, 0xa8304613, 0xfd469501,
		0x698098d8, 0x8b44f7af, 0xffff5bb1, 0x895cd7be, 0x6b901122, 0xfd987193, 0xa679438e, 0x49b40821,
		0xf61e2562, 0xc040b340, 0x265e5a51, 0xe9b6c7aa, 0xd62f105d, 0x02441453, 0xd8a1e681, 0xe7d3fbc8,
		0x21e1cde6, 0xc33707d6, 0xf4d50d87, 0x455a14ed, 0xa9e3e905, 0xfcefa3f8, 0x676f02d9, 0x8d2a4c8a,
		0xfffa3942, 0x8771f681, 0x6d9d6122, 0xfde5380c, 0xa4beea44, 0x4bdecfa9, 0xf6bb4b60, 0xbebfbc70,
		0x289b7ec6, 0xeaa127fa, 0xd4ef3085, 0x04881d05, 0xd9d4d039, 0xe6db99e5, 0x1fa27cf8, 0xc4ac5665,
		0xf4292244, 0x432aff97, 0xab9423a7, 0xfc93a039, 0x655b59c3, 0x8f0ccc92, 0xffeff47d, 0x85845dd1,
		0x6fa87e4f, 0xfe2ce6e0, 0xa3014314, 0x4e0811a1, 0xf7537e82, 0xbd3af235, 0x2ad7d2bb, 0xeb86d391,
	};

	private static readonly int[] Md5R =
	{
		7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
		5,  9, 14, 20, 5,  9, 14, 20, 5,  9, 14, 20, 5,  9, 14, 20,
		4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
		6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
	};
}
