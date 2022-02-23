﻿using System;
using CodeHelpers.Diagnostics;
using CodeHelpers.Mathematics;
using EchoRenderer.Common;
using EchoRenderer.Common.Mathematics;
using EchoRenderer.Common.Mathematics.Primitives;
using EchoRenderer.Common.Memory;
using EchoRenderer.Core.Aggregation.Primitives;
using EchoRenderer.Core.Scenic.Instancing;
using EchoRenderer.Core.Scenic.Preparation;

namespace EchoRenderer.Core.Aggregation.Preparation;

public class PreparedInstance
{
	/// <summary>
	/// Creates a regular <see cref="PreparedInstance"/>.
	/// </summary>
	public PreparedInstance(ScenePreparer preparer, PackInstance instance, uint id) : this(preparer, instance.EntityPack, instance.Swatch, id)
	{
		inverseTransform = instance.LocalToWorld;
		forwardTransform = instance.WorldToLocal;

		Float3 scale = instance.Scale;

		inverseScale = scale.Average;
		forwardScale = 1f / inverseScale;

		if ((Float3)inverseScale != scale) throw new Exception($"{nameof(PackInstance)} does not support none uniform scaling! '{scale}'");
	}

	protected PreparedInstance(ScenePreparer preparer, EntityPack pack, MaterialSwatch swatch, uint id)
	{
		this.id = id;

		this.pack = preparer.GetPreparedPack(pack, out SwatchExtractor extractor, out NodeTokenArray tokenArray);
		this.swatch = extractor.Prepare(swatch);

		powerDistribution = CreatePowerDistribution(this.pack, this.swatch, tokenArray);
	}

	/// <summary>
	/// Computes the <see cref="AxisAlignedBoundingBox"/> of all contents inside this instance based on its parent's coordinate system.
	/// This <see cref="AxisAlignedBoundingBox"/> does not necessary enclose the root, only the enclosure of the content is guaranteed.
	/// NOTE: This property could be slow, so if performance issues arise try to memoize the result.
	/// </summary>
	public AxisAlignedBoundingBox AABB => pack.aggregator.GetTransformedAABB(inverseTransform);

	public readonly uint id;
	public readonly PreparedPack pack;
	public readonly PreparedSwatch swatch;

	public readonly Float4x4 forwardTransform = Float4x4.identity; //The parent to local transform
	public readonly Float4x4 inverseTransform = Float4x4.identity; //The local to parent transform

	readonly float forwardScale = 1f; //The parent to local scale multiplier
	readonly float inverseScale = 1f; //The local to parent scale multiplier

	readonly PowerDistribution powerDistribution;

	/// <summary>
	/// Returns the total emissive power of this <see cref="PreparedInstance"/>.
	/// </summary>
	public float Power => powerDistribution.Total * inverseScale * inverseScale;

	/// <summary>
	/// Processes <paramref name="query"/>.
	/// </summary>
	public void Trace(ref TraceQuery query)
	{
		var oldRay = query.ray;

		//Convert from parent space to local space
		TransformForward(ref query.ray);
		query.distance *= forwardScale;
		query.current.Push(this);

		//Gets intersection from aggregator in local space
		pack.aggregator.Trace(ref query);

		//Convert back to parent space
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

		//Convert from parent space to local space
		TransformForward(ref query.ray);
		query.travel *= forwardScale;
		query.current.Push(this);

		//Gets intersection from aggregator in local space
		if (pack.aggregator.Occlude(ref query)) return true;

		//Convert back to parent space
		query.travel *= inverseScale;
		query.ray = oldRay;
		query.current.Pop();

		return false;
	}

	/// <summary>
	/// Returns the cost of tracing a <see cref="TraceQuery"/>.
	/// </summary>
	public int TraceCost(Ray ray, ref float distance)
	{
		//Forward transform distance to local space
		distance *= forwardScale;

		//Gets intersection cost, calculation done in local space
		TransformForward(ref ray);

		int cost = pack.aggregator.TraceCost(ray, ref distance);

		//Restore distance back to parent space
		distance *= inverseScale;
		return cost;
	}

	/// <summary>
	/// Transforms <paramref name="ray"/> from parent to local space.
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
	static PowerDistribution CreatePowerDistribution(PreparedPack pack, PreparedSwatch swatch, NodeTokenArray tokenArray)
	{
		int powerLength = 0;
		int segmentLength = 0;

		var materials = swatch.EmissiveIndices;
		var instances = tokenArray[tokenArray.FinalPartition];

		//Iterate through materials to find their partition lengths
		foreach (MaterialIndex index in materials)
		{
			var partition = tokenArray[index];
			Assert.IsFalse(partition.IsEmpty);
			powerLength += partition.Length;
		}

		segmentLength += materials.Length;

		//Iterate through instances to see if any is emissive
		foreach (NodeToken token in instances)
		{
			var instance = pack.GetInstance(token.InstanceValue);
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
			float power = PackedMath.GetLuminance(Utilities.ToVector(swatch[index].Emission));

			foreach (NodeToken token in tokenArray[index])
			{
				float area = pack.GetArea(token);
				powerFill.Add(area * power);
			}

			segmentFill.Add(index);
		}

		//Fill in the power values for instances if any is emissive
		if (!segmentFill.IsFull)
		{
			foreach (NodeToken token in instances)
			{
				powerFill.Add(pack.GetInstance(token.InstanceValue).Power);
			}

			segmentFill.Add(tokenArray.FinalPartition);
		}

		//Create power distribution instance
		Assert.IsTrue(segmentFill.IsFull);
		Assert.IsTrue(powerFill.IsFull);

		return new PowerDistribution(powerValues, segments, tokenArray);
	}
}