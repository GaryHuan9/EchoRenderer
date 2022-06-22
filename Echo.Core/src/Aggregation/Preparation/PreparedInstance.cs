﻿using System;
using CodeHelpers.Diagnostics;
using CodeHelpers.Mathematics;
using CodeHelpers.Packed;
using Echo.Core.Aggregation.Bounds;
using Echo.Core.Aggregation.Primitives;
using Echo.Core.Common.Mathematics;
using Echo.Core.Common.Mathematics.Primitives;
using Echo.Core.Common.Memory;
using Echo.Core.Evaluation.Distributions;
using Echo.Core.Evaluation.Materials;
using Echo.Core.Scenic.Instancing;
using Echo.Core.Scenic.Preparation;

namespace Echo.Core.Aggregation.Preparation;

public class PreparedInstance
{
	/// <summary>
	/// Creates a regular <see cref="PreparedInstance"/>.
	/// </summary>
	public PreparedInstance(ScenePreparer preparer, PackInstance instance, EntityToken token) : this(preparer, instance.EntityPack, instance.Swatch, token)
	{
		forwardTransform = instance.ForwardTransform;
		inverseTransform = instance.InverseTransform;

		float scale = new Float3(forwardTransform.f00, forwardTransform.f01, forwardTransform.f02).Magnitude;

		inverseScale = scale;
		forwardScale = 1f / scale;
	}

	protected PreparedInstance(ScenePreparer preparer, EntityPack pack, MaterialSwatch swatch, EntityToken token)
	{
		this.token = token;

		this.pack = preparer.GetPreparedPack(pack, out SwatchExtractor extractor, out EntityTokenArray tokenArray);
		this.swatch = extractor.Prepare(swatch);

		powerDistribution = CreatePowerDistribution(this.pack, this.swatch, tokenArray);
	}

	/// <summary>
	/// Computes the <see cref="AxisAlignedBoundingBox"/> of all contents inside this instance based on its parent's coordinate system.
	/// This <see cref="AxisAlignedBoundingBox"/> does not necessary enclose the root, only the enclosure of the content is guaranteed.
	/// NOTE: This property could be slow, so if performance issues arise try to memoize the result.
	/// </summary>
	public AxisAlignedBoundingBox AABB => pack.aggregator.GetTransformedAABB(inverseTransform);

	public readonly EntityToken token;
	public readonly PreparedPack pack;

	public readonly Float4x4 forwardTransform = Float4x4.identity; //The parent to local transform
	public readonly Float4x4 inverseTransform = Float4x4.identity; //The local to parent transform

	protected readonly PreparedSwatch swatch;

	readonly float forwardScale = 1f; //The parent to local scale multiplier
	readonly float inverseScale = 1f; //The local to parent scale multiplier

	readonly PowerDistribution powerDistribution;

	/// <summary>
	/// Returns the total emissive power of this <see cref="PreparedInstance"/>.
	/// </summary>
	public float Power => powerDistribution?.Total * inverseScale * inverseScale ?? 0f;

	/// <summary>
	/// Processes <paramref name="query"/>.
	/// </summary>
	public void Trace(ref TraceQuery query)
	{
		var oldRay = query.ray;

		//Convert from parent-space to local-space
		TransformForward(ref query.ray);
		query.distance *= forwardScale;
		query.current.Push(this);

		//Gets intersection from aggregator in local-space
		pack.aggregator.Trace(ref query);

		//Convert back to parent-space
		query.distance *= inverseScale;
		query.ray = oldRay;
		query.current.Pop();
	}

	/// <summary>
	/// Processes <paramref name="query"/> and returns the result.
	/// </summary>
	public bool Occlude(ref OccludeQuery query)
	{
		var oldRay = query.ray;

		//Convert from parent-space to local-space
		TransformForward(ref query.ray);
		query.travel *= forwardScale;
		query.current.Push(this);

		//Gets intersection from aggregator in local-space
		if (pack.aggregator.Occlude(ref query)) return true;

		//Convert back to parent-space
		query.travel *= inverseScale;
		query.ray = oldRay;
		query.current.Pop();

		return false;
	}

	/// <summary>
	/// Returns the <see cref="Material"/> represented by <paramref name="index"/> in this <see cref="PreparedInstance"/>.
	/// </summary>
	public Material GetMaterial(MaterialIndex index) => swatch[index];

	/// <summary>
	/// Picks an emissive geometry from this <see cref="PreparedInstance"/>.
	/// </summary>
	/// <param name="sample">The <see cref="Sample1D"/> value used to select the target emissive geometry.</param>
	/// <param name="instance">Outputs the <see cref="PreparedInstance"/> that immediately holds this geometry.</param>
	/// <returns>The picked <see cref="Probable{T}"/> of type <see cref="TokenHierarchy"/> that represents our emissive geometry.</returns>
	/// <remarks>If <see cref="Power"/> is not positive, the behavior of this method is undefined.</remarks>
	public Probable<TokenHierarchy> Pick(Sample1D sample, out PreparedInstance instance)
	{
		Assert.IsTrue(FastMath.Positive(Power));
		var geometryToken = new TokenHierarchy();

		instance = this;
		float pdf = 1f;

		do
		{
			// ReSharper disable once LocalVariableHidesMember
			(EntityToken token, float tokenPdf) = instance.powerDistribution.Pick(ref sample);

			Assert.IsTrue(token.Type.IsGeometry());

			if (token.Type.IsRawGeometry()) geometryToken.TopToken = token;
			else geometryToken.Push(instance = instance.pack.GetInstance(token));

			pdf *= tokenPdf;
		}
		while (geometryToken.TopToken == default);

		return (geometryToken, pdf);
	}

	/// <summary>
	/// Returns the cost of tracing a <see cref="TraceQuery"/>.
	/// </summary>
	public uint TraceCost(Ray ray, ref float distance)
	{
		//Forward transform distance to local-space
		distance *= forwardScale;

		//Gets intersection cost, calculation done in local-space
		TransformForward(ref ray);

		uint cost = pack.aggregator.TraceCost(ray, ref distance);

		//Restore distance back to parent-space
		distance *= inverseScale;
		return cost;
	}

	/// <summary>
	/// Transforms <paramref name="ray"/> from parent to local-space.
	/// </summary>
	void TransformForward(ref Ray ray)
	{
		Float3 origin = forwardTransform.MultiplyPoint(ray.origin);
		Float3 direction = forwardTransform.MultiplyDirection(ray.direction);

		ray = new Ray(origin, direction * inverseScale);
	}

	/// <summary>
	/// Creates and returns a new <see cref="powerDistribution"/> for some emissive
	/// objects indicated by the parameter. If no object is emissive, null is returned.
	/// </summary>
	static PowerDistribution CreatePowerDistribution(PreparedPack pack, PreparedSwatch swatch, EntityTokenArray tokenArray)
	{
		int powerLength = 0;
		int segmentLength = 0;

		var materials = swatch.EmissiveIndices;
		var instances = GetInstancesToken();

		//Iterate through materials to find their partition lengths
		foreach (MaterialIndex index in materials)
		{
			var partition = tokenArray[index];
			Assert.IsFalse(partition.IsEmpty);
			powerLength += partition.Length;
		}

		segmentLength += materials.Length;

		//Iterate through instances to see if any is emissive
		foreach (EntityToken token in instances)
		{
			PreparedInstance instance = pack.GetInstance(token);
			if (!FastMath.Positive(instance.Power)) continue;

			powerLength += instances.Length;
			++segmentLength;

			break;
		}

		//Exit if nothing is emissive
		if (powerLength == 0) return null;
		Assert.IsTrue(segmentLength > 0);

		//Fetch buffers
		using var _0 = Pool<float>.Fetch(powerLength, out var powerValues);
		using var _1 = Pool<int>.Fetch(segmentLength, out var segments);

		SpanFill<float> powerFill = powerValues;
		SpanFill<int> segmentFill = segments;

		//Fill in the relevant power and segment values for geometries with emissive materials
		foreach (MaterialIndex index in materials)
		{
			float power = ((IEmissive)swatch[index]).Power;
			Assert.IsTrue(FastMath.Positive(power));

			foreach (EntityToken token in tokenArray[index]) powerFill.Add(pack.GetArea(token) * power);

			segmentFill.Add(index);
		}

		//Fill in the power values for instances if any is emissive
		if (!segmentFill.IsFull)
		{
			foreach (EntityToken token in instances) powerFill.Add(pack.GetInstance(token).Power);

			segmentFill.Add(tokenArray.FinalPartition);
		}

		//Create power distribution instance
		Assert.IsTrue(segmentFill.IsFull);
		Assert.IsTrue(powerFill.IsFull);

		return new PowerDistribution(powerValues, segments, tokenArray);

		ReadOnlyView<EntityToken> GetInstancesToken() => pack.counts.instance > 0 ? tokenArray[tokenArray.FinalPartition] : ReadOnlyView<EntityToken>.Empty;
	}
}