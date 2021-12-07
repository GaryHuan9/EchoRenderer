﻿using EchoRenderer.Mathematics;

namespace EchoRenderer.Rendering.Distributions
{
	/// <summary>
	/// A sample on an one dimensional distribution between zero (inclusive) and one (exclusive)
	/// </summary>
	public readonly struct Distro1
	{
		public Distro1(float u) => this.u = FastMath.ClampEpsilon(u);

		public readonly float u;
	}
}