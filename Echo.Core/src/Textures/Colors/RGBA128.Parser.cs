﻿using System;
using CodeHelpers;
using CodeHelpers.Diagnostics;

namespace Echo.Core.Textures.Colors;

partial struct RGBA128
{
	readonly ref struct Parser
	{
		public Parser(ReadOnlySpan<char> span)
		{
			span = span.Trim();

			if (RemovePrefix(ref span, "0x"))
			{
				type = Type.Hex;
			}
			else if (RemovePrefix(ref span, "#"))
			{
				type = Type.Hex;
				RemovePrefix(ref span, "#");
			}
			else
			{
				if (!RemovePrefix(ref span, "rgb"))
				{
					RemovePrefix(ref span, "hdr");
					type = Type.HDR;
				}
				else type = Type.RGB;

				if (!RemoveParenthesis(ref span)) type = Type.Error;
			}

			content = span;

			static bool RemovePrefix(ref ReadOnlySpan<char> span, ReadOnlySpan<char> prefix)
			{
				Assert.IsTrue(prefix.Length > 0);

				if (!span.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

				span = span[prefix.Length..];
				return true;
			}

			static bool RemoveParenthesis(ref ReadOnlySpan<char> span)
			{
				if (span[0] != '(' || span[^1] != ')') return false;

				span = span[1..^1].Trim();
				return true;
			}
		}

		readonly Type type;
		readonly ReadOnlySpan<char> content;

		public bool Execute(out RGBA128 result) => type switch
		{
			Type.Hex   => ParseHex(out result),
			Type.RGB   => ParseRGB(out result),
			Type.HDR   => ParseHDR(out result),
			Type.Error => YieldError(out result),
			_          => throw ExceptionHelper.Invalid(nameof(type), type, InvalidType.unexpected)
		};

		bool ParseHex(out RGBA128 result)
		{
			int r;
			int g;
			int b;
			int a = 255;

			switch (content.Length)
			{
				case 1:
				{
					if (ConvertHex(content[0], out int value))
					{
						r = g = b = CombineHex(value, value);
						break;
					}

					goto default;
				}
				case 3:
				{
					if (ConvertHex(content[0], out r) &&
						ConvertHex(content[1], out g) &&
						ConvertHex(content[2], out b))
					{
						r = CombineHex(r, r);
						g = CombineHex(g, g);
						b = CombineHex(b, b);
						break;
					}

					goto default;
				}
				case 4:
				{
					if (ConvertHex(content[3], out a))
					{
						a = CombineHex(a, a);
						goto case 3;
					}

					goto default;
				}
				case 6:
				{
					if (ConvertHex(content[0], out int r0) && ConvertHex(content[1], out int r1) &&
						ConvertHex(content[2], out int g0) && ConvertHex(content[3], out int g1) &&
						ConvertHex(content[4], out int b0) && ConvertHex(content[5], out int b1))
					{
						r = CombineHex(r0, r1);
						g = CombineHex(g0, g1);
						b = CombineHex(b0, b1);
						break;
					}

					goto default;
				}
				case 8:
				{
					if (ConvertHex(content[6], out int a0) && ConvertHex(content[7], out int a1))
					{
						a = CombineHex(a0, a1);
						goto case 6;
					}

					goto default;
				}
				default: return YieldError(out result);
			}

			return ConvertRGBA(r, g, b, a, out result);

			static int CombineHex(int hex0, int hex1) => hex0 * 16 + hex1;
		}

		bool ParseRGB(out RGBA128 result)
		{
			int head = 0;

			if (FindNextInt(content, ref head, out int r) &&
				FindNextInt(content, ref head, out int g) &&
				FindNextInt(content, ref head, out int b))
			{
				if (!FindNextInt(content, ref head, out int a, alpha: true)) return YieldError(out result);

				return ConvertRGBA(r, g, b, a, out result);
			}

			return YieldError(out result);

			static bool FindNextInt(ReadOnlySpan<char> source, ref int head, out int result, bool alpha = false)
			{
				if (alpha)
				{
					result = 255;
					if (head >= source.Length) return true; // no alpha is given so set alpha to 255 and return
				}

				while (head < source.Length && !char.IsDigit(source[head]))
				{
					if (source.Length <= head++)
					{
						result = 0;
						return false; // find first digit
					}
				}

				int tail = head;
				while (tail < source.Length && char.IsDigit(source[tail])) tail++; // get the length of the number

				if (tail == head)
				{
					result = 0;
					return false;
				}

				bool parsed = int.TryParse(source[head..tail], out result);
				head = tail;
				return parsed;
			}
		}

		bool ParseHDR(out RGBA128 result)
		{
			try
			{
				int index = 0;

				if (FindNextFloat(content, ref index, out float r) &&
					FindNextFloat(content, ref index, out float g) &&
					FindNextFloat(content, ref index, out float b))
				{
					if (!FindNextFloat(content, ref index, out float a, alpha: true)) return YieldError(out result);

					result = new RGBA128(r, g, b, a);
					return true;
				}

				return YieldError(out result);

			}
			catch
			{
				return YieldError(out result);
			}

			static bool FindNextFloat(ReadOnlySpan<char> source, ref int head, out float result, bool alpha = false)
			{
				if (alpha)
				{
					result = 1f;
					if (head >= source.Length) return true; // no alpha is given so set alpha to 1f and return
				}

				while (head < source.Length && !char.IsDigit(source[head]))
				{
					if (source.Length <= head++)
					{
						result = 0f;
						return false; // find first digit
					}
				}

				int tail = head;
				while (tail < source.Length && (char.IsDigit(source[tail]) || source[tail].Equals('.'))) tail++; // get the length of the number

				if (tail == head)
				{
					result = 0f;
					return false;
				}

				bool parsed = float.TryParse(source[head..tail], out result);
				head = tail;
				return parsed;
			}
		}

		static bool ConvertRGBA(int r, int g, int b, int a, out RGBA128 result)
		{
			if (ConvertChannel(r, out float channel0) &&
				ConvertChannel(g, out float channel1) &&
				ConvertChannel(b, out float channel2) &&
				ConvertChannel(a, out float channel3))
			{
				result = new RGBA128(channel0, channel1, channel2, channel3);
				return true;
			}

			return YieldError(out result);

			static bool ConvertChannel(int value, out float result)
			{
				if ((uint)value >= 256)
				{
					result = 0f;
					return false;
				}

				result = value / 255f;
				return true;
			}
		}

		static bool ConvertHex(char digit, out int value)
		{
			value = digit switch
			{
				>= '0' and <= '9' => digit - '0',
				>= 'A' and <= 'F' => digit - 'A' + 10,
				>= 'a' and <= 'f' => digit - 'a' + 10,
				_                 => -1
			};

			if (value >= 0) return true;

			value = 0;
			return false;
		}

		static bool YieldError(out RGBA128 result)
		{
			result = Zero;
			return false;
		}

		enum Type : byte
		{
			Hex,
			RGB,
			HDR,
			Error
		}
	}
}