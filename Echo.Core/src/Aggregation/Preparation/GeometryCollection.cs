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

		static ImmutableArray<T> Extract<T>(SwatchExtractor swatchExtractor, ReadOnlySpan<IGeometrySource> geometrySources) where T : IPreparedPureGeometry
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

	public ImmutableArray<T> GetArray<T>() where T : IPreparedGeometry
	{
		if (typeof(T) == typeof(PreparedTriangle)) return Unsafe.As<ImmutableArray<PreparedTriangle>, ImmutableArray<T>>(ref Unsafe.AsRef(in triangles));
	}

	public View<Tokenized<AxisAlignedBoundingBox>> CreateBoundsView()
	{
		var result = new Tokenized<AxisAlignedBoundingBox>[counts.Total];
		var fill = result.AsFill();

		Add(ref fill, TokenType.Triangle, triangles);
		Add(ref fill, TokenType.Sphere, spheres);
		Add(ref fill, TokenType.Instance, instances);

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

	/// <summary>
	/// Creates a new <see cref="Touch"/> from <paramref name="query"/>. The <see cref="Material"/> is extracted from <paramref name="swatch"/>.
	/// The world to local <see cref="Float4x4"/> for the geometry found with <paramref name="query"/> should be passed through <paramref name="transform"/>.
	/// </summary>
	public Touch Interact(in TraceQuery query, PreparedSwatch swatch, in Float4x4 transform)
	{
		EntityToken token = query.token.TopToken;
		Assert.IsTrue(token.Type.IsRawGeometry());

		Float3 normal;
		Float2 texcoord;
		MaterialIndex materialIndex;

		switch (token.Type)
		{
			case TokenType.Triangle:
			{
				ref readonly PreparedTriangle triangle = ref triangles.ItemRef(token.Index);

				normal = triangle.GetNormal(query.uv);
				texcoord = triangle.GetTexcoord(query.uv);
				materialIndex = triangle.Material;

				break;
			}
			case TokenType.Sphere:
			{
				ref readonly PreparedSphere sphere = ref spheres.ItemRef(token.Index);

				normal = PreparedSphere.GetNormal(query.uv);
				texcoord = PreparedSphere.GetTexcoord(query.uv);
				materialIndex = sphere.Material;

				break;
			}
			default: throw new ArgumentOutOfRangeException(nameof(query));
		}

		normal = transform.MultiplyDirection(normal).Normalized; //Apply world transform to normal
		Material material = swatch[materialIndex];               //Find appropriate mapped material

		//Construct touch
		if (material == null) return new Touch(query, normal);
		return new Touch(query, normal, material, texcoord);
	}

	/// <summary>
	/// Returns the <see cref="PreparedInstance"/> stored in this <see cref="PreparedPack"/> represented by <paramref name="token"/>.
	/// </summary>
	public PreparedInstance GetInstance(EntityToken token)
	{
		Assert.AreEqual(token.Type, TokenType.Instance);
		return instances[token.Index];
	}

	/// <summary>
	/// Returns the <see cref="MaterialIndex"/> of the geometry represented by <paramref name="token"/>.
	/// </summary>
	public MaterialIndex GetMaterialIndex(EntityToken token)
	{
		Assert.IsTrue(token.Type.IsRawGeometry());

		return token.Type switch
		{
			TokenType.Triangle => triangles[token.Index].Material,
			TokenType.Sphere   => spheres[token.Index].Material,
			_                  => throw new ArgumentOutOfRangeException(nameof(token))
		};
	}

	/// <summary>
	/// Returns the area of the geometry represented by <paramref name="token"/>.
	/// </summary>
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

	/// <summary>
	/// Underlying implementation of <see cref="PreparedSceneOld.Sample"/>, functional
	/// according to the local coordinate system of this <see cref="PreparedPack"/>.
	/// </summary>
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

	/// <summary>
	/// Underlying implementation of <see cref="PreparedSceneOld.ProbabilityDensity"/>, functional
	/// according to the local coordinate system of this <see cref="PreparedPack"/>.
	/// </summary>
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