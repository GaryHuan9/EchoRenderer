﻿using CodeHelpers.Packed;
using Echo.Core.Aggregation.Primitives;
using Echo.Core.Common.Mathematics.Primitives;
using Echo.Core.Common.Memory;
using Echo.Core.Evaluation.Distributions.Continuous;
using Echo.Core.Evaluation.Materials;
using Echo.Core.Scenic.Preparation;
using Echo.Core.Textures.Colors;
using Echo.Core.Textures.Evaluation;

namespace Echo.Core.Evaluation.Evaluators;

public record BruteForcedEvaluator : Evaluator
{
	/// <summary>
	/// The maximum number of bounces a path can have before it is immediately terminated unconditionally.
	/// If such occurrence appears, the sample becomes biased and this property should be increased.
	/// </summary>
	public int BounceLimit { get; init; } = 128;

	public override Float4 Evaluate(PreparedSceneOld scene, in Ray ray, ContinuousDistribution distribution, Allocator allocator)
	{
		int depth = BounceLimit;
		var query = new TraceQuery(ray);

		return Evaluate(scene, ref query, distribution, allocator, ref depth);
	}

	public override IEvaluationLayer CreateOrClearLayer(RenderBuffer buffer) => CreateOrClearLayer<RGB128>(buffer, "force");

	static Float4 Evaluate(PreparedSceneOld scene, ref TraceQuery query, ContinuousDistribution distribution, Allocator allocator, ref int depth)
	{
		if (--depth <= 0) return RGB128.Black;

		allocator.Restart();

		while (scene.Trace(ref query))
		{
			Touch touch = scene.Interact(query);
			Material material = touch.shade.material;
			material.Scatter(ref touch, allocator);

			if (touch.bsdf != null)
			{
				var emission = RGB128.Black;

				if (material is IEmissive emissive) emission += emissive.Emit(touch.point, touch.outgoing);

				var scatterSample = distribution.Next2D();
				Probable<RGB128> sample = touch.bsdf.Sample
				(
					touch.outgoing, scatterSample,
					out Float3 incident, out _
				);

				if (sample.NotPossible | sample.content.IsZero) return emission;

				RGB128 scatter = sample.content / sample.pdf;
				scatter *= touch.NormalDot(incident);
				query = query.SpawnTrace(incident);

				return scatter * Evaluate(scene, ref query, distribution, allocator, ref depth) + emission;
			}

			query = query.SpawnTrace();
		}

		return scene.lights.EvaluateAmbient(query.ray.direction);
	}

}