﻿using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using CodeHelpers.Diagnostics;
using CodeHelpers.Mathematics;
using CodeHelpers.Packed;
using Echo.Core.Aggregation.Bounds;
using Echo.Core.Aggregation.Primitives;
using Echo.Core.Common.Mathematics.Primitives;
using Echo.Core.Common.Memory;
using Echo.Core.Evaluation.Distributions;
using Echo.Core.Evaluation.Materials;
using Echo.Core.Scenic.Geometric;
using Echo.Core.Scenic.Preparation;

namespace Echo.Core.Aggregation.Preparation;

public sealed class GeometryCollection
{
	public GeometryCollection(SwatchExtractor swatchExtractor, ReadOnlySpan<IGeometrySource> geometrySources, ImmutableArray<PreparedInstance> instances)
	{
		triangles = Extract<PreparedTriangle>(swatchExtractor, geometrySources);
		spheres = Extract<PreparedSphere>(swatchExtractor, geometrySources);
		this.instances = instances;

		swatch = swatchExtractor.Prepare();
		counts = new GeometryCounts(triangles.Length, spheres.Length, instances.Length);

		static ImmutableArray<T> Extract<T>(SwatchExtractor swatchExtractor, ReadOnlySpan<IGeometrySource> geometrySources) where T : IPreparedGeometry
		{
			int length = 0;

			foreach (IGeometrySource source in geometrySources)
			{
				if (source is not IGeometrySource<T> match) continue;
				length += (int)match.Count;
			}

			var builder = ImmutableArray.CreateBuilder<T>(length);

			foreach (IGeometrySource source in geometrySources)
			{
				if (source is not IGeometrySource<T> match) continue;

				int expected = builder.Count + (int)match.Count;
				builder.AddRange(match.Extract(swatchExtractor));

				if (expected != builder.Count) throw new Exception($"{nameof(IGeometrySource<T>.Count)} mismatch on {source}.");
			}

			return builder.MoveToImmutable();
		}
	}

	public readonly ImmutableArray<PreparedTriangle> triangles;
	public readonly ImmutableArray<PreparedSphere> spheres;
	public readonly ImmutableArray<PreparedInstance> instances;

	public readonly PreparedSwatch swatch;
	public readonly GeometryCounts counts;

	public View<Tokenized<AxisAlignedBoundingBox>> CreateBoundsView()
	{
		var result = new Tokenized<AxisAlignedBoundingBox>[counts.Total];
		var fill = result.AsFill();

		Add(ref fill, TokenType.Triangle, triangles);
		Add(ref fill, TokenType.Sphere, spheres);

		for (int i = 0; i < instances.Length; i++)
		{
			ref readonly PreparedInstance instance = ref instances.ItemRef(i);
			fill.Add((new EntityToken(TokenType.Instance, i), instance.AABB));
		}

		Assert.IsTrue(fill.IsFull);
		return result;

		void Add<T>(ref SpanFill<Tokenized<AxisAlignedBoundingBox>> fill, TokenType type, ImmutableArray<T> array) where T : IPreparedGeometry
		{
			for (int i = 0; i < array.Length; i++)
			{
				ref readonly T geometry = ref array.ItemRef(i);
				fill.Add((new EntityToken(type, i), geometry.AABB));
			}
		}
	}

	/// <summary>
	/// Calculates the intersection between <paramref name="query"/> and the object represented by <paramref name="token"/>.
	/// </summary>
	/// <remarks>The intersection is only considered if it occurs before the original <see cref="TraceQuery.distance"/>.</remarks>
	public void Trace(EntityToken token, ref TraceQuery query)
	{
		Assert.IsTrue(token.Type.IsGeometry());

		switch (token.Type)
		{
			case TokenType.Triangle:
			{
				query.current.TopToken = token;
				if (query.ignore == query.current) return;

				ref readonly var triangle = ref triangles.ItemRef(token.Index);
				float distance = triangle.Intersect(query.ray, out Float2 uv);

				if (distance >= query.distance) return;

				query.token = query.current;
				query.distance = distance;
				query.uv = uv;

				break;
			}
			case TokenType.Sphere:
			{
				query.current.TopToken = token;
				bool findFar = query.ignore == query.current;

				ref readonly PreparedSphere sphere = ref spheres.ItemRef(token.Index);
				float distance = sphere.Intersect(query.ray, out Float2 uv, findFar);

				if (distance >= query.distance) return;

				query.token = query.current;
				query.distance = distance;
				query.uv = uv;

				break;
			}
			case TokenType.Instance:
			{
				query.current.Push(token);
				instances[token.Index].Trace(ref query);

				EntityToken popped = query.current.Pop();
				Assert.AreEqual(popped, token);
				break;
			}
			default: throw new ArgumentOutOfRangeException(nameof(token));
		}
	}

	/// <summary>
	/// Calculates and returns whether <paramref name="query"/> is occluded by the object represented by <paramref name="token"/>.
	/// </summary>
	/// <remarks>The intersection is only considered if it occurs before the original <see cref="OccludeQuery.travel"/>.</remarks>
	public bool Occlude(EntityToken token, ref OccludeQuery query)
	{
		Assert.IsTrue(token.Type.IsGeometry());

		switch (token.Type)
		{
			case TokenType.Triangle:
			{
				query.current.TopToken = token;
				if (query.ignore == query.current) return false;

				return triangles[token.Index].Intersect(query.ray, query.travel);
			}
			case TokenType.Sphere:
			{
				query.current.TopToken = token;
				bool findFar = query.ignore == query.current;

				return spheres[token.Index].Intersect(query.ray, query.travel, findFar);
			}
			case TokenType.Instance:
			{
				query.current.Push(token);
				if (instances[token.Index].Occlude(ref query)) return true;

				EntityToken popped = query.current.Pop();
				Assert.AreEqual(popped, token);
				return false;
			}
			default: throw new ArgumentOutOfRangeException(nameof(token));
		}
	}

	/// <summary>
	/// Returns the cost of an intersection calculation between <paramref name="ray"/> and the object represented by <paramref name="token"/>.
	/// </summary>
	public uint GetTraceCost(in Ray ray, ref float distance, EntityToken token)
	{
		Assert.IsTrue(token.Type.IsGeometry());

		switch (token.Type)
		{
			case TokenType.Triangle:
			{
				distance = Math.Min(distance, triangles[token.Index].Intersect(ray, out _));
				return 1;
			}
			case TokenType.Sphere:
			{
				distance = Math.Min(distance, spheres[token.Index].Intersect(ray, out _));
				return 1;
			}
			case TokenType.Instance:
			{
				return instances[token.Index].TraceCost(ray, ref distance);
			}
			default: throw new ArgumentOutOfRangeException(nameof(token));
		}
	}

	public Contact.Info GetContactInfo(EntityToken token, Float2 uv)
	{
		Assert.IsTrue(token.Type.IsRawGeometry());

		switch (token.Type)
		{
			case TokenType.Triangle:
			{
				ref readonly PreparedTriangle triangle = ref triangles.ItemRef(token.Index);

				return new Contact.Info
				(
					triangle.Material,
					triangle.GetNormal(uv),
					triangle.GetTexcoord(uv)
				);
			}
			case TokenType.Sphere:
			{
				ref readonly PreparedSphere sphere = ref spheres.ItemRef(token.Index);

				return new Contact.Info
				(
					sphere.Material,
					PreparedSphere.GetNormal(uv),
					PreparedSphere.GetTexcoord(uv)
				);
			}
			default: throw new ArgumentOutOfRangeException(nameof(token));
		}
	}

	/// <inheritdoc cref="IPreparedGeometry.Material"/>
	public MaterialIndex GetMaterial(EntityToken token)
	{
		Assert.IsTrue(token.Type.IsRawGeometry());

		return token.Type switch
		{
			TokenType.Triangle => triangles[token.Index].Material,
			TokenType.Sphere   => spheres[token.Index].Material,
			_                  => throw new ArgumentOutOfRangeException(nameof(token))
		};
	}

	/// <inheritdoc cref="IPreparedGeometry.Area"/>
	public float GetArea(EntityToken token)
	{
		Assert.IsTrue(token.Type.IsRawGeometry());

		return token.Type switch
		{
			TokenType.Triangle => triangles[token.Index].Area,
			TokenType.Sphere   => spheres[token.Index].Area,
			_                  => throw new ArgumentOutOfRangeException(nameof(token))
		};
	}

	/// <inheritdoc cref="IPreparedGeometry.Sample"/>
	public Probable<GeometryPoint> Sample(EntityToken token, in Float3 origin, Sample2D sample)
	{
		Assert.IsTrue(token.Type.IsRawGeometry());

		return token.Type switch
		{
			TokenType.Triangle => triangles[token.Index].Sample(origin, sample),
			TokenType.Sphere   => spheres[token.Index].Sample(origin, sample),
			_                  => throw new ArgumentOutOfRangeException(nameof(token))
		};
	}

	/// <inheritdoc cref="IPreparedGeometry.ProbabilityDensity"/>
	public float ProbabilityDensity(EntityToken token, in Float3 origin, in Float3 incident)
	{
		Assert.IsTrue(token.Type.IsRawGeometry());

		return token.Type switch
		{
			TokenType.Triangle => triangles[token.Index].ProbabilityDensity(origin, incident),
			TokenType.Sphere   => spheres[token.Index].ProbabilityDensity(origin, incident),
			_                  => throw new ArgumentOutOfRangeException(nameof(token))
		};
	}
}