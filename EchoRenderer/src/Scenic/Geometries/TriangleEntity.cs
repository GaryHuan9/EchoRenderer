﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using CodeHelpers;
using CodeHelpers.Diagnostics;
using CodeHelpers.Mathematics;
using EchoRenderer.Mathematics;
using EchoRenderer.Mathematics.Primitives;
using EchoRenderer.Rendering.Distributions;
using EchoRenderer.Rendering.Materials;
using EchoRenderer.Scenic.Preparation;

namespace EchoRenderer.Scenic.Geometries;

public class TriangleEntity : GeometryEntity
{
	public TriangleEntity(Material material, Float3 vertex0, Float3 vertex1, Float3 vertex2) : base(material)
	{
		Vertex0 = vertex0;
		Vertex1 = vertex1;
		Vertex2 = vertex2;
	}

	public Float3 Vertex0 { get; set; }
	public Float3 Vertex1 { get; set; }
	public Float3 Vertex2 { get; set; }

	public Float3 Normal0 { get; set; }
	public Float3 Normal1 { get; set; }
	public Float3 Normal2 { get; set; }

	public override IEnumerable<PreparedTriangle> ExtractTriangles(SwatchExtractor extractor)
	{
		uint materialToken = extractor.Register(Material);

		if (Normal0 == Float3.zero || Normal1 == Float3.zero || Normal2 == Float3.zero)
		{
			yield return new PreparedTriangle
			(
				LocalToWorld.MultiplyPoint(Vertex0), LocalToWorld.MultiplyPoint(Vertex1), LocalToWorld.MultiplyPoint(Vertex2),
				materialToken
			);
		}
		else
		{
			yield return new PreparedTriangle
			(
				LocalToWorld.MultiplyPoint(Vertex0), LocalToWorld.MultiplyPoint(Vertex1), LocalToWorld.MultiplyPoint(Vertex2),
				LocalToWorld.MultiplyDirection(Normal0), LocalToWorld.MultiplyDirection(Normal1), LocalToWorld.MultiplyDirection(Normal2),
				materialToken
			);
		}
	}

	public override IEnumerable<PreparedSphere> ExtractSpheres(SwatchExtractor extractor) => Enumerable.Empty<PreparedSphere>();
}

public readonly struct PreparedTriangle //Winding order for triangles is CLOCKWISE
{
	public PreparedTriangle(Float3 vertex0, Float3 vertex1, Float3 vertex2, uint materialToken) : this
	(
		vertex0, vertex1, vertex2,
		Float3.Cross(vertex1 - vertex0, vertex2 - vertex0), materialToken
	) { }

	public PreparedTriangle(Float3 vertex0, Float3 vertex1, Float3 vertex2, Float3 normal, uint materialToken) : this
	(
		vertex0, vertex1, vertex2,
		normal, normal, normal, materialToken
	) { }

	public PreparedTriangle(Float3 vertex0, Float3 vertex1, Float3 vertex2,
							Float2 texcoord0, Float2 texcoord1, Float2 texcoord2, uint materialToken) : this
	(
		vertex0, vertex1, vertex2,
		Float3.Cross(vertex1 - vertex0, vertex2 - vertex0),
		texcoord0, texcoord1, texcoord2, materialToken
	) { }

	public PreparedTriangle(Float3 vertex0, Float3 vertex1, Float3 vertex2,
							Float3 normal,
							Float2 texcoord0, Float2 texcoord1, Float2 texcoord2, uint materialToken) : this
	(
		vertex0, vertex1, vertex2,
		normal, normal, normal,
		texcoord0, texcoord1, texcoord2, materialToken
	) { }

	public PreparedTriangle(Float3 vertex0, Float3 vertex1, Float3 vertex2,
							Float3 normal0, Float3 normal1, Float3 normal2, uint materialToken) : this
	(
		vertex0, vertex1, vertex2,
		normal0, normal1, normal2,
		Float2.zero, Float2.zero, Float2.zero, materialToken
	) { }

	public PreparedTriangle(Float3 vertex0, Float3 vertex1, Float3 vertex2,
							Float3 normal0, Float3 normal1, Float3 normal2,
							Float2 texcoord0, Float2 texcoord1, Float2 texcoord2, uint materialToken)
	{
		this.vertex0 = vertex0;
		edge1 = vertex1 - vertex0;
		edge2 = vertex2 - vertex0;

		this.normal0 = normal0.Normalized;
		this.normal1 = normal1.Normalized;
		this.normal2 = normal2.Normalized;

		this.texcoord0 = texcoord0;
		this.texcoord1 = texcoord1;
		this.texcoord2 = texcoord2;

		this.materialToken = materialToken;
	}

	public readonly Float3 vertex0; //NOTE: Vertex one and two are actually not needed for intersection
	public readonly Float3 edge1;
	public readonly Float3 edge2;

	public readonly Float3 normal0;
	public readonly Float3 normal1;
	public readonly Float3 normal2;

	public readonly Float2 texcoord0;
	public readonly Float2 texcoord1;
	public readonly Float2 texcoord2;

	public readonly uint materialToken;

	public Float3 Vertex1 => vertex0 + edge1;
	public Float3 Vertex2 => vertex0 + edge2;

	/// <summary>
	/// The smallest <see cref="AxisAlignedBoundingBox"/> that encloses this <see cref="PreparedTriangle"/>.
	/// </summary>
	public AxisAlignedBoundingBox AABB => new(stackalloc[] { vertex0, Vertex1, Vertex2 });

	/// <summary>
	/// The area of this <see cref="PreparedTriangle"/>.
	/// </summary>
	public float Area => Float3.Cross(edge1, edge2).Magnitude / 2f;

	/// <summary>
	/// Returns the distance of intersection between this <see cref="PreparedTriangle"/> and <paramref name="ray"/> without
	/// backface culling. If the intersection exists, the distance is returned and <paramref name="uv"/> will contain the
	/// barycentric coordinate of the intersection, otherwise, <see cref="float.PositiveInfinity"/> is returned.
	/// The famous Möller–Trumbore algorithm: https://cadxfem.org/inf/Fast%20MinimumStorage%20RayTriangle%20Intersection.pdf
	/// </summary>
	public float Intersect(in Ray ray, out Float2 uv)
	{
		const float Infinity = float.PositiveInfinity;
		Unsafe.SkipInit(out uv);

		//Calculate determinant and u
		Float3 cross2 = Float3.Cross(ray.direction, edge2);
		float determinant = Float3.Dot(edge1, cross2);

		//If determinant is close to zero, ray is parallel to triangle
		if (determinant == 0f) return Infinity;
		float determinantR = 1f / determinant;

		Float3 offset = ray.origin - vertex0;
		float u = offset.Dot(cross2) * determinantR;

		//Check if is outside barycentric bounds
		if (u is < 0f or > 1f) return Infinity;

		Float3 cross1 = Float3.Cross(offset, edge1);
		float v = ray.direction.Dot(cross1) * determinantR;

		//Check if is outside barycentric bounds
		if (v < 0f || u + v > 1f) return Infinity;

		//Check if ray is pointing away from triangle
		float distance = edge2.Dot(cross1) * determinantR;
		if (distance < 0f) return Infinity;

		uv = new Float2(u, v);
		return distance;
	}

	/// <summary>
	/// Returns whether <paramref name="ray"/> will intersect with this <see cref="PreparedTriangle"/> after <paramref name="travel"/>.
	/// The famous Möller–Trumbore algorithm: https://cadxfem.org/inf/Fast%20MinimumStorage%20RayTriangle%20Intersection.pdf
	/// </summary>
	public bool Intersect(in Ray ray, float travel)
	{
		//Calculate determinant and u
		Float3 cross2 = Float3.Cross(ray.direction, edge2);
		float determinant = Float3.Dot(edge1, cross2);

		//If determinant is close to zero, ray is parallel to triangle
		if (determinant == 0f) return false;
		float sign = MathF.Sign(determinant);
		determinant *= sign;

		Float3 offset = ray.origin - vertex0;
		float u = offset.Dot(cross2) * sign;

		//Check if is outside barycentric bounds
		if (u < 0f || u > determinant) return false;

		Float3 cross1 = Float3.Cross(offset, edge1);
		float v = ray.direction.Dot(cross1) * sign;

		//Check if is outside barycentric bounds
		if (v < 0f || u + v > determinant) return false;

		//Check if ray is pointing away from triangle
		float distance = edge2.Dot(cross1) * sign;
		return distance >= 0f && distance < travel * determinant;
	}

	/// <summary>
	/// Uniformly samples this <see cref="PreparedTriangle"/> based on <paramref name="distro"/> and outputs
	/// the probability density function <paramref name="pdf"/> over solid angles from <paramref name="origin"/>.
	/// </summary>
	public GeometryPoint Sample(in Float3 origin, Distro2 distro, out float pdf)
	{
		Float2 uv = distro.UniformTriangle;
		Float3 position = GetPoint(uv);
		Float3 normal = GetPoint(uv);

		var point = new GeometryPoint(position, normal);
		pdf = point.ProbabilityDensity(origin, Area);

		return point;
	}

	/// <summary>
	/// Returns the probability density function over solid angles of sampling <paramref name="incident"/> from <paramref name="origin"/>.
	/// </summary>
	public float ProbabilityDensity(in Float3 origin, in Float3 incident)
	{
		Ray ray = new Ray(origin, incident);

		float distance = Intersect(ray, out Float2 uv);
		if (float.IsPositiveInfinity(distance)) return 0f;
		Assert.AreEqual(incident.SquaredMagnitude, 1f);

		return distance * distance / FastMath.Abs(GetNormal(uv).Dot(incident) * Area);
	}

	public Float3 GetNormal(Float2 uv) => ((1f - uv.x - uv.y) * normal0 + uv.x * normal1 + uv.y * normal2).Normalized;
	public Float2 GetTexcoord(Float2 uv) => (1f - uv.x - uv.y) * texcoord0 + uv.x * texcoord1 + uv.y * texcoord2;

	public void GetSubdivided(Span<PreparedTriangle> triangles, int iteration)
	{
		int requiredLength = 1 << (iteration * 2);

		if (triangles.Length == requiredLength)
		{
			triangles[0] = this;
			GetSubdivided(triangles, normal0, normal1, normal2);
		}
		else throw ExceptionHelper.Invalid(nameof(triangles), triangles.Length, $"is not long enough! Need at least {requiredLength}!");
	}

	public override string ToString() => $"<{nameof(vertex0)}: {vertex0}, {nameof(Vertex1)}: {Vertex1}, {nameof(Vertex2)}: {Vertex2}>";

	Float3 GetPoint(Float2 uv) => vertex0 + uv.x * edge1 + uv.y * edge2;

	static void GetSubdivided(Span<PreparedTriangle> triangles, in Float3 normal00, in Float3 normal11, in Float3 normal22)
	{
		if (triangles.Length <= 1) return;

		//The uv locations right in the middle of two vertices
		Float2 uv01 = new Float2(0.5f, 0f);
		Float2 uv02 = new Float2(0f, 0.5f);
		Float2 uv12 = new Float2(0.5f, 0.5f);

		//Begin dividing triangle
		ref readonly PreparedTriangle triangle = ref triangles[0];

		Float3 normal01 = GetInterpolatedNormal(uv01, normal00, normal11, normal22);
		Float3 normal02 = GetInterpolatedNormal(uv02, normal00, normal11, normal22);
		Float3 normal12 = GetInterpolatedNormal(uv12, normal00, normal11, normal22);

		Float3 vertex01 = triangle.GetPoint(uv01);
		Float3 vertex02 = triangle.GetPoint(uv02);
		Float3 vertex12 = triangle.GetPoint(uv12);

		Float3 vertex00 = triangle.vertex0;
		Float3 vertex11 = triangle.Vertex1;
		Float3 vertex22 = triangle.Vertex2;

		Float2 texcoord01 = triangle.GetTexcoord(uv01);
		Float2 texcoord02 = triangle.GetTexcoord(uv02);
		Float2 texcoord12 = triangle.GetTexcoord(uv12);

		Float2 texcoord00 = triangle.texcoord0;
		Float2 texcoord11 = triangle.texcoord1;
		Float2 texcoord22 = triangle.texcoord2;

		Fill(triangles, 0, triangle.materialToken, vertex01, vertex12, vertex02, normal01, normal12, normal02, texcoord01, texcoord12, texcoord02);
		Fill(triangles, 1, triangle.materialToken, vertex00, vertex01, vertex02, normal00, normal01, normal02, texcoord00, texcoord01, texcoord02);
		Fill(triangles, 2, triangle.materialToken, vertex11, vertex12, vertex01, normal11, normal12, normal01, texcoord11, texcoord12, texcoord01);
		Fill(triangles, 3, triangle.materialToken, vertex22, vertex02, vertex12, normal22, normal02, normal12, texcoord22, texcoord02, texcoord12);

		//NOTE: this normal is not normalized, because normalized normals will mess up during subdivision
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static Float3 GetInterpolatedNormal(Float2 uv, in Float3 normal00, in Float3 normal11, in Float3 normal22) => (1f - uv.x - uv.y) * normal00 + uv.x * normal11 + uv.y * normal22;

		static void Fill(Span<PreparedTriangle> span, int index, uint materialToken,
						 in Float3 vertex0, in Float3 vertex1, in Float3 vertex2,
						 in Float3 normal0, in Float3 normal1, in Float3 normal2,
						 Float2 texcoord0, Float2 texcoord1, Float2 texcoord2)
		{
			int gap = span.Length / 4;
			var slice = span.Slice(gap * index, gap);

			slice[0] = new PreparedTriangle
			(
				vertex0, vertex1, vertex2,
				normal0, normal1, normal2,
				texcoord0, texcoord1, texcoord2, materialToken
			);

			GetSubdivided(slice, normal0, normal1, normal2);
		}
	}
}