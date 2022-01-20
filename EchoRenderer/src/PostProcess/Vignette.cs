﻿using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using CodeHelpers.Mathematics;
using EchoRenderer.Common;
using EchoRenderer.Mathematics.Randomization;

namespace EchoRenderer.PostProcess
{
	public class Vignette : PostProcessingWorker
	{
		public Vignette(PostProcessingEngine engine) : base(engine) { }

		public float Intensity { get; set; } = 0.57f;
		public float FilmGrain { get; set; } = 0.01f; //A little bit of film grain helps with the color banding

		public override void Dispatch() => RunPassHorizontal(HorizontalPass);

		void HorizontalPass(int horizontal)
		{
			IRandom random = new SystemRandom((uint)horizontal);

			for (int y = 0; y < renderBuffer.size.y; y++)
			{
				Int2 position = new Int2(horizontal, y);
				Float2 uv = position * renderBuffer.sizeR;

				float distance = (uv - Float2.half).SquaredMagnitude * Intensity;
				float multiplier = 1f + random.Next1(-FilmGrain, FilmGrain) - distance;

				Vector128<float> target = PackedMath.Clamp01(renderBuffer[position]);
				renderBuffer[position] = Sse.Multiply(target, Vector128.Create(multiplier));
			}
		}
	}
}