﻿using System;
using System.Runtime.Intrinsics;
using CodeHelpers.Mathematics;
using EchoRenderer.Mathematics.Primitives;

namespace EchoRenderer.Mathematics.Intersections
{
	/// <summary>
	/// A simple linear aggregator. Utilities four-wide SIMD parallelization.
	/// Optimal for small numbers of geometries and tokens, but works with any.
	/// </summary>
	public class LinearAggregator : Aggregator
	{
		public LinearAggregator(PressedPack pack, ReadOnlyMemory<AxisAlignedBoundingBox> aabbs, ReadOnlySpan<Token> tokens) : base(pack)
		{
			Validate(aabbs, tokens);

			ReadOnlySpan<AxisAlignedBoundingBox> span = aabbs.Span;
			nodes = new Node[span.Length.CeiledDivide(Width)];

			for (int i = 0; i < nodes.Length; i++)
			{
				int index = i * Width;

				nodes[i] = new Node(span[index..], tokens[index..]);
			}

			totalCount = span.Length;
		}

		readonly Node[] nodes;
		readonly int totalCount;

		/// <summary>
		/// We store four <see cref="AxisAlignedBoundingBox"/> and tokens in one <see cref="Node"/>.
		/// </summary>
		const int Width = 4;

		public override void Trace(ref TraceQuery query)
		{
			foreach (ref readonly Node node in nodes.AsSpan())
			{
				Vector128<float> intersections = node.aabb4.Intersect(query.ray);

				for (int i = 0; i < Width; i++)
				{
					if (intersections.GetElement(i) >= query.distance) continue;
					ref readonly Token token = ref node.token4[i];
					pack.GetIntersection(ref query, token);
				}
			}
		}

		public override void Occlude(ref OccludeQuery query)
		{
			throw new NotImplementedException();
		}

		public override int TraceCost(in Ray ray, ref float distance)
		{
			int cost = nodes.Length * Width;

			foreach (ref readonly Node node in nodes.AsSpan())
			{
				Vector128<float> intersections = node.aabb4.Intersect(ray);

				for (int i = 0; i < Width; i++)
				{
					if (intersections.GetElement(i) >= distance) continue;
					ref readonly Token token = ref node.token4[i];
					cost += pack.GetIntersectionCost(ray, ref distance, token);
				}
			}

			return cost;
		}

		public override unsafe int GetHashCode()
		{
			fixed (Node* ptr = nodes) return Utilities.GetHashCode(ptr, (uint)nodes.Length, totalCount);
		}

		protected override int FillAABB(uint depth, Span<AxisAlignedBoundingBox> span)
		{
			//If theres enough room to store every individual AABB
			if (span.Length >= totalCount)
			{
				int index = 0;

				foreach (ref readonly Node node in nodes.AsSpan())
				{
					for (int i = 0; i < Width; i++)
					{
						if (index == totalCount) break;
						span[index++] = node.aabb4[i];
					}
				}

				return index;
			}

			Span<AxisAlignedBoundingBox> aabb4 = stackalloc AxisAlignedBoundingBox[Width];

			//If there is enough space to store AABBs that enclose every node's AABB4
			if (span.Length >= nodes.Length)
			{
				for (int i = 0; i < nodes.Length; i++)
				{
					ref readonly Node node = ref nodes[i];

					int count = Math.Min(totalCount - i * Width, Width);
					Span<AxisAlignedBoundingBox> slice = aabb4[count..];

					for (int j = 0; j < count; j++) slice[j] = node.aabb4[j];

					span[i] = new AxisAlignedBoundingBox(slice);
				}

				return nodes.Length;
			}

			//Finally, store all enclosure AABBs and then one last big AABB that encloses all the remaining AABBs
			for (int i = 0; i < span.Length - 1; i++)
			{
				ref readonly Node node = ref nodes[i];

				for (int j = 0; j < Width; j++) aabb4[j] = node.aabb4[j];

				span[i] = new AxisAlignedBoundingBox(aabb4);
			}

			Float3 min = Float3.positiveInfinity;
			Float3 max = Float3.negativeInfinity;

			for (int i = span.Length; i < nodes.Length; i++)
			{
				ref readonly Node node = ref nodes[i];

				int count = Math.Min(totalCount - i * Width, Width);

				for (int j = 0; j < count; j++)
				{
					AxisAlignedBoundingBox aabb = node.aabb4[j];

					min = min.Min(aabb.min);
					max = max.Max(aabb.max);
				}
			}

			span[^1] = new AxisAlignedBoundingBox(min, max);

			return span.Length;
		}

		readonly struct Node
		{
			public Node(ReadOnlySpan<AxisAlignedBoundingBox> aabbs, ReadOnlySpan<Token> tokens)
			{
				aabb4 = new AxisAlignedBoundingBox4(aabbs);
				token4 = new Token4(tokens);
			}

			public readonly AxisAlignedBoundingBox4 aabb4;
			public readonly Token4 token4;
		}
	}
}