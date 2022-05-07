﻿using Echo.Core.Evaluation.Distributions.Continuous;
using Echo.Core.Scenic;

namespace Echo.Core.Evaluation.Distributions;

/// <summary>
/// A combination of <see cref="Sample2D"/> and <see cref="Sample1D"/> that defines the sampling for a <see cref="Camera"/>.
/// </summary>
public readonly struct CameraSample
{
	public CameraSample(ContinuousDistribution distribution)
	{
		uv = distribution.Next2D();
		time = distribution.Next1D();
	}

	public readonly Sample2D uv;
	public readonly Sample1D time;
}