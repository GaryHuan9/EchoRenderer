﻿using System;
using CodeHelpers.Diagnostics;
using CodeHelpers.Mathematics;

namespace EchoRenderer.Core.Rendering.Distributions;

public class StratifiedDistribution : LimitedDistribution
{
	public StratifiedDistribution(Int2 sampleSize, int dimensionCount) : base(sampleSize.Product, dimensionCount) => this.sampleSize = sampleSize;

	StratifiedDistribution(StratifiedDistribution source) : base(source)
	{
		sampleSize = source.sampleSize;
		Jitter = source.Jitter;
	}

	public readonly Int2 sampleSize;

	/// <summary>
	/// Returns whether the stratified samples are randomly shifted inside their individual cells.
	/// </summary>
	public bool Jitter { get; set; } = true;

	public override void BeginPixel(Int2 position)
	{
		base.BeginPixel(position);
		Assert.IsNotNull(Random);

		//Fill single samples
		foreach (SpanAggregate<Sample1D> aggregate in singleOnes)
		{
			FillStratum(aggregate.array);
			Random.Shuffle<Sample1D>(aggregate.array);
		}

		foreach (SpanAggregate<Sample2D> aggregate in singleTwos)
		{
			FillStratum(aggregate.array, sampleSize);
			Random.Shuffle<Sample2D>(aggregate.array);
		}
	}

	public override void BeginSample()
	{
		base.BeginSample();

		//Fill span samples
		for (int i = 0; i < arrayOnes.Count; i++)
		{
			Span<Sample1D> span = arrayOnes[i][SampleNumber];

			FillStratum(span);
			Random.Shuffle(span);
		}

		for (int i = 0; i < arrayTwos.Count; i++)
		{
			LatinHypercube(arrayTwos[i][SampleNumber]);
		}
	}

	void FillStratum(Span<Sample1D> span)
	{
		float scale = 1f / span.Length;

		for (int i = 0; i < span.Length; i++)
		{
			ref Sample1D sample = ref span[i];
			float offset = Jitter ? Random.Next1() : 0.5f;
			sample = (Sample1D)((i + offset) * scale);
		}
	}

	void FillStratum(Span<Sample2D> span, Int2 size)
	{
		Assert.AreEqual(span.Length, size.Product);
		Float2 scale = 1f / size;

		Int2 position = Int2.zero;

		for (int i = 0; i < span.Length; i++)
		{
			ref Sample2D sample = ref span[i];

			Float2 offset = Jitter ? Random.Next2() : Float2.half;
			sample = (Sample2D)((position + offset) * scale);

			if (position.x < size.x - 1) position += Int2.right;
			else position = new Int2(0, position.y + 1);
		}
	}

	void LatinHypercube(Span<Sample2D> span)
	{
		int length = span.Length;
		float scale = 1f / length;

		Span<float> spanX = stackalloc float[length];
		Span<float> spanY = stackalloc float[length];

		for (int i = 0; i < length; i++) spanX[i] = i;
		for (int i = 0; i < length; i++) spanY[i] = i;

		Random.Shuffle(spanX);
		Random.Shuffle(spanY);

		for (int i = 0; i < span.Length; i++)
		{
			ref Sample2D sample = ref span[i];
			Float2 offset = Jitter ? Random.Next2() : Float2.half;
			Float2 position = new Float2(spanX[i], spanY[i]);
			sample = (Sample2D)((position + offset) * scale);
		}
	}

	public override ContinuousDistribution Replicate() => new StratifiedDistribution(this);
}