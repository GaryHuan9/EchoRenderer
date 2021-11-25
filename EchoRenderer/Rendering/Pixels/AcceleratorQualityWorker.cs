﻿using System.Threading;
using CodeHelpers.Mathematics;
using EchoRenderer.Mathematics.Intersections;
using EchoRenderer.Rendering.Memory;
using EchoRenderer.Rendering.Profiles;

namespace EchoRenderer.Rendering.Pixels
{
	public class AcceleratorQualityWorker : PixelWorker
	{
		long totalCost;
		long totalSample;

		public override void BeforeRender(RenderProfile profile)
		{
			base.BeforeRender(profile);

			Interlocked.Exchange(ref totalCost, 0);
			Interlocked.Exchange(ref totalSample, 0);
		}

		public override Sample Render(Float2 screenUV, Arena arena)
		{
			PressedScene scene = arena.profile.Scene;
			Ray ray = scene.camera.GetRay(screenUV);

			int cost = scene.GetIntersectionCost(ray);

			long currentCost = Interlocked.Add(ref totalCost, cost);
			long currentSample = Interlocked.Increment(ref totalSample);

			return new Float3(cost, currentCost, currentSample);
		}
	}
}