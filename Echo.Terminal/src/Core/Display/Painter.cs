﻿using System;
using CodeHelpers.Diagnostics;
using CodeHelpers.Packed;

namespace Echo.Terminal.Core.Display;

using CharSpan = ReadOnlySpan<char>;

public readonly struct Painter
{
	public Painter(Int2 size, char[] array, int stride, int offset)
	{
		Assert.IsTrue(size > Int2.Zero);
		Assert.IsNotNull(array);

		this.size = size;
		this.array = array;
		this.stride = stride;
		this.offset = offset;
	}

	/// <summary>
	/// The width the height of the drawable space.
	/// </summary>
	public readonly Int2 size;

	readonly char[] array;
	readonly int stride;
	readonly int offset;

	/// <summary>
	/// Accesses a location.
	/// </summary>
	/// <param name="position">The location to access.</param>
	public ref char this[Int2 position] => ref this[position.X, position.Y];

	/// <summary>
	/// Accesses a location.
	/// </summary>
	/// <param name="x">The X location to access.</param>
	/// <param name="y">The Y location to access.</param>
	public ref char this[int x, int y]
	{
		get
		{
			Assert.IsTrue(new Int2(x, y) >= Int2.Zero);
			Assert.IsTrue(new Int2(x, y) < size);
			return ref array[y * stride + x + offset];
		}
	}

	/// <summary>
	/// Accesses a horizontal row through a <see cref="Span{T}"/>.
	/// </summary>
	/// <param name="y">The Y location of the row to access.</param>
	public Span<char> this[int y]
	{
		get
		{
			Assert.IsTrue(0 <= y && y < size.Y);
			return array.AsSpan(y * stride + offset, size.X);
		}
	}

	/// <summary>
	/// Fills everything with one <see cref="char"/>.
	/// </summary>
	/// <param name="value">The <see cref="char"/> to fill.</param>
	public void FillAll(char value = ' ') => FillAll(0, value);

	/// <summary>
	/// Fills all available space with one <see cref="char"/>.
	/// </summary>
	/// <param name="y">The Y location to begin filling.</param>
	/// <param name="value">The <see cref="char"/> to fill.</param>
	/// <returns>The Y location that the filling finished; this will always be out of bounds.</returns>
	public int FillAll(int y, char value = ' ')
	{
		if (CheckBounds(y)) return y;
		for (; y < size.Y; y++) FillLine(y, value);
		return y;
	}

	/// <summary>
	/// Fills all available space with one <see cref="char"/>.
	/// </summary>
	/// <param name="position">The location to begin filling.</param>
	/// <param name="value">The <see cref="char"/> to fill.</param>
	/// <returns>The location that the filling finished; this will always be out of bounds.</returns>
	public Int2 FillAll(Int2 position, char value = ' ')
	{
		position = FillLine(position, value);
		return new Int2(0, FillAll(position.Y));
	}

	/// <summary>
	/// Fills an entire horizontal line with one <see cref="char"/>.
	/// </summary>
	/// <param name="y">The Y location to begin filling.</param>
	/// <param name="value">The <see cref="char"/> to fill.</param>
	/// <returns>The Y location that the filling finished; this will always be on a new line.</returns>
	public int FillLine(int y, char value = ' ')
	{
		if (CheckBounds(y)) return y;
		this[y].Fill(value);
		return y + 1;
	}

	/// <summary>
	/// Fills an entire horizontal line with one <see cref="char"/>.
	/// </summary>
	/// <param name="position">The location to begin filling.</param>
	/// <param name="value">The <see cref="char"/> to fill.</param>
	/// <returns>The location that the filling finished; this will always be on a new line.</returns>
	public Int2 FillLine(Int2 position, char value = ' ')
	{
		if (CheckBounds(position)) return position;
		this[position.Y][position.X..].Fill(value);
		return NextLine(position);
	}

	/// <summary>
	/// Writes a string of texts and ends in a new line.
	/// </summary>
	/// <param name="y">The Y location to start writing.</param>
	/// <param name="texts">The text to write.</param>
	/// <param name="options">Options used to format the texts.</param>
	/// <returns>The T location the writing ends; this will always be on a new line.</returns>
	public int WriteLine(int y, CharSpan texts, in TextOptions options = default) => WriteLine(new Int2(0, y), texts, options).Y;

	/// <summary>
	/// Writes a string of texts and ends in a new line.
	/// </summary>
	/// <param name="position">The location to start writing.</param>
	/// <param name="texts">The text to write.</param>
	/// <param name="options">Options used to format the texts.</param>
	/// <returns>The location the writing ends; this will always be on a new line.</returns>
	public Int2 WriteLine(Int2 position, CharSpan texts, in TextOptions options = default)
	{
		position = Write(position, texts, options);
		if (position.X == 0) return position;
		return FillLine(position);
	}

	/// <summary>
	/// Writes a string of texts.
	/// </summary>
	/// <param name="y">The Y location to start writing.</param>
	/// <param name="texts">The text to write.</param>
	/// <param name="options">Options used to format the texts.</param>
	/// <returns>The Y location the writing ends.</returns>
	public int Write(int y, CharSpan texts, in TextOptions options = default) => Write(new Int2(0, y), texts, options).Y;

	/// <summary>
	/// Writes a string of texts.
	/// </summary>
	/// <param name="position">The location to start writing.</param>
	/// <param name="texts">The text to write.</param>
	/// <param name="options">Options used to format the texts.</param>
	/// <returns>The location the writing ends.</returns>
	public Int2 Write(Int2 position, CharSpan texts, in TextOptions options = default)
	{
		if (CheckBounds(position)) return position;

		WrapOptions wrap = options.WrapOptions;
		Span<char> span = this[position.Y][position.X..];
		if (wrap == WrapOptions.NoWrap) goto finalLine;

		while (texts.Length > span.Length && position.Y + 1 < size.Y)
		{
			if (wrap == WrapOptions.LineBreak)
			{
				texts[..span.Length].CopyTo(span);
				texts = texts[span.Length..];
			}
			else
			{
				int end = span.Length;

				if (!char.IsWhiteSpace(texts[end]))
				{
					int index = LastIndexOfWhiteSpace(texts[..end]);
					if (index >= 0) end = index;
				}

				switch (wrap)
				{
					case WrapOptions.Justified:
					{
						JustifiedCopy(texts[..end], span);
						break;
					}
					case WrapOptions.WordBreak:
					{
						OverwriteCopy(texts[..end], span);
						break;
					}
					case WrapOptions.LineBreak:
					case WrapOptions.NoWrap:
					default: throw new ArgumentOutOfRangeException(nameof(options));
				}

				texts = texts[end..].TrimStart();
			}

			NextLine(ref position);
			span = this[position.Y];
		}

	finalLine:
		if (texts.Length > span.Length)
		{
			texts[..span.Length].CopyTo(span);
			if (!options.Truncate) span[^1] = '…';
			return NextLine(position);
		}

		if (texts.Length == span.Length)
		{
			texts.CopyTo(span);
			return NextLine(position);
		}

		if (texts.IsEmpty) return position;
		position += new Int2(texts.Length, 0);

		OverwriteCopy(texts, span);
		return position;
	}

	bool CheckBounds(int y)
	{
		if ((0 <= y) & (y <= size.Y)) return y == size.Y;
		throw new ArgumentOutOfRangeException(nameof(y));
	}

	bool CheckBounds(Int2 position)
	{
		if ((Int2.Zero <= position) & (position < size)) return false;
		if ((position.X == 0) & (position.Y == size.Y)) return true;
		throw new ArgumentOutOfRangeException(nameof(position));
	}

	static int LastIndexOfWhiteSpace(ReadOnlySpan<char> span)
	{
		for (int i = span.Length - 1; i >= 0; i--)
		{
			if (char.IsWhiteSpace(span[i])) return i;
		}

		return -1;
	}

	static void NextLine(ref Int2 position) => position = NextLine(position);
	static Int2 NextLine(Int2 position) => new(0, position.Y + 1);

	static void OverwriteCopy(CharSpan source, Span<char> target)
	{
		source.CopyTo(target);
		if (target.Length == source.Length) return;
		target[source.Length..].Fill(' ');
	}

	static void JustifiedCopy(CharSpan source, Span<char> target)
	{
		source = source.TrimEnd();
		Assert.IsTrue(source.Length <= target.Length);
		CopyStart(ref source, ref target, true);

		Count(source, out int wordCount, out int charCount);

		if (wordCount <= 1)
		{
			OverwriteCopy(source, target);
			return;
		}

		int gap = wordCount - 1;               //The number of gaps between words in source
		int space = target.Length - charCount; //The total count of spaces to be added
		int average = space / gap;             //The average number of spaces per gap (rounded down)
		int remain = space - average * gap;    //The remain number of spaces because of the round down

		for (int i = 0; i < gap; i++)
		{
			CopyStart(ref source, ref target, false);
			int count = average + (i < remain ? 1 : 0);

			target[..count].Fill(' ');
			target = target[count..];
			source = source.TrimStart();
		}

		Assert.AreEqual(source.Length, target.Length);
		source.CopyTo(target);

		static void CopyStart(ref CharSpan source, ref Span<char> target, bool whiteSpace)
		{
			int index = 0;

			for (; index < source.Length; index++)
			{
				char value = source[index];
				if (char.IsWhiteSpace(value) != whiteSpace) break;
				target[index] = value;
			}

			if (index == 0) return;
			source = source[index..];
			target = target[index..];
		}

		static void Count(CharSpan source, out int wordCount, out int charCount)
		{
			wordCount = 0;
			charCount = 0;

			bool wasSpace = true;

			foreach (char value in source)
			{
				bool isSpace = char.IsWhiteSpace(value);
				if (!isSpace & wasSpace) ++wordCount;
				if (!isSpace) ++charCount;

				wasSpace = isSpace;
			}
		}
	}
}