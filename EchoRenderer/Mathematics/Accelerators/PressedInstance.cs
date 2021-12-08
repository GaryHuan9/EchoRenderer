﻿using System;
using CodeHelpers.Diagnostics;
using CodeHelpers.Mathematics;
using EchoRenderer.Mathematics.Intersections;
using EchoRenderer.Objects;
using EchoRenderer.Objects.Scenes;

namespace EchoRenderer.Mathematics.Accelerators
{
	public class PressedInstance
	{
		/// <summary>
		/// Creates a regular <see cref="PressedInstance"/>.
		/// </summary>
		public PressedInstance(ScenePresser presser, ObjectInstance instance) : this(presser, instance.ObjectPack, instance.Mapper)
		{
			forwardTransform = instance.WorldToLocal;
			inverseTransform = instance.LocalToWorld;

			Float3 scale = instance.Scale;

			if (scale.Sorted == scale.SortedReversed)
			{
				inverseScale = scale.Average;
				forwardScale = 1f / inverseScale;
			}
			else throw new Exception($"{nameof(ObjectInstance)} does not support none uniform scaling! '{scale}'");
		}

		/// <summary>
		/// Creates a root <see cref="PressedInstance"/> with <paramref name="scene"/>.
		/// </summary>
		public PressedInstance(ScenePresser presser, Scene scene) : this(presser, scene, null)
		{
			forwardTransform = Float4x4.identity;
			inverseTransform = Float4x4.identity;

			forwardScale = 1f;
			inverseScale = 1f;
		}

		PressedInstance(ScenePresser presser, ObjectPack pack, MaterialMapper mapper)
		{
			id = presser.RegisterPressedPackInstance(this);
			this.pack = presser.GetPressedPack(pack);
			this.mapper = presser.materials.GetMapper(mapper);
		}

		/// <summary>
		/// Computes the AABB of all contents inside this instance based on its parent's coordinate system.
		/// This AABB does not necessary enclose the root, only the enclosure of the content is guaranteed.
		/// NOTE: This property could be slow, so if performance issues arise try to memoize the result.
		/// </summary>
		public AxisAlignedBoundingBox AABB
		{
			get
			{
				const uint FetchDepth = 6; //How deep do we go into the accelerator to get the AABB of the nodes
				Span<AxisAlignedBoundingBox> aabbs = stackalloc AxisAlignedBoundingBox[1 << (int)FetchDepth];

				int count = pack.accelerator.FillAABB(FetchDepth, aabbs);
				Float4x4 absoluteTransform = inverseTransform.Absoluted;

				Float3 min = Float3.positiveInfinity;
				Float3 max = Float3.negativeInfinity;

				//Find a small AABB by encapsulating children nodes instead of the full accelerator
				for (int i = 0; i < count; i++)
				{
					ref readonly var aabb = ref aabbs[i];

					Float3 center = inverseTransform.MultiplyPoint(aabb.Center);
					Float3 extend = absoluteTransform.MultiplyDirection(aabb.Extend);

					min = min.Min(center - extend);
					max = max.Max(center + extend);
				}

				return new AxisAlignedBoundingBox(min, max);
			}
		}

		public readonly uint id;
		public readonly PressedPack pack;
		public readonly MaterialPresser.Mapper mapper;

		readonly Float4x4 forwardTransform; //The parent to local transform matrix
		readonly Float4x4 inverseTransform; //The local to parent transform matrix

		readonly float forwardScale; //The parent to local scale multiplier
		readonly float inverseScale; //The local to parent scale multiplier

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

			//Gets intersection from accelerator in local space
			pack.accelerator.Trace(ref query);

			//Convert back to parent space
			query.ray = oldRay;
			query.distance *= inverseScale;
			query.current.Pop();
		}

		/// <summary>
		/// Processes <paramref name="query"/> as a <see cref="PressedInstance"/> root.
		/// </summary>
		public void TraceRoot(ref TraceQuery query)
		{
			Assert.IsTrue(query.current.Equals(default));
			pack.accelerator.Trace(ref query);
		}

		/// <summary>
		/// Returns the cost of tracing a <see cref="TraceQuery"/>.
		/// </summary>
		public int TraceCost(in Ray ray, ref float distance)
		{
			//Forward transform distance to local space
			distance *= forwardScale;

			//Gets intersection cost from bvh, calculation done in local space
			Ray transformed = ray;
			TransformForward(ref transformed);

			int cost = pack.accelerator.TraceCost(transformed, ref distance);

			//Transforms distance back to parent space
			distance *= inverseScale;
			return cost;
		}

		/// <summary>
		/// Transforms <paramref name="direction"/> from local space to parent space.
		/// NOTE: this might apply a uniformed scaling to <paramref name="direction"/>.
		/// </summary>
		public void TransformInverse(ref Float3 direction)
		{
			direction = inverseTransform.MultiplyDirection(direction);
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
	}
}