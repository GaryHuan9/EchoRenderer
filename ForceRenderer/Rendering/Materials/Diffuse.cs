﻿using CodeHelpers.Mathematics;
using ForceRenderer.Mathematics;

namespace ForceRenderer.Rendering.Materials
{
	public class Diffuse : Material
	{
		public override Float3 BidirectionalScatter(in CalculatedHit hit, ExtendedRandom random, out Float3 direction)
		{
			if (AlphaTest(hit, out Float3 color, out direction)) return Float3.one;
			direction = (hit.normal + random.NextOnSphere()).Normalized;

			return color;
		}
	}
}