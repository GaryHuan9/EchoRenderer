﻿using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using CodeHelpers.Mathematics;
using EchoRenderer.IO;
using EchoRenderer.Mathematics.Accelerators;
using EchoRenderer.Mathematics.Intersections;
using EchoRenderer.Objects.GeometryObjects;
using EchoRenderer.Objects.Scenes;
using EchoRenderer.Rendering;
using EchoRenderer.Rendering.Materials;
using EchoRenderer.Rendering.Profiles;

namespace EchoRenderer.Tests
{
	public class BenchmarkBVH
	{
		public BenchmarkBVH()
		{
			Scene scene = new Scene();
			Random random = new Random(42);

			Mesh mesh = new(@"C:\Users\MMXXXVIII\Things\CodingStuff\C#\EchoRenderer\EchoRenderer\Assets\Models\BlenderBMW\BlenderBMW.obj");
			scene.children.Add(new MeshObject(mesh, new Glossy()));

			queries = new HitQuery[65536];

			const float Radius = 18f;
			const float Height = 12f;

			for (int i = 0; i < queries.Length; i++)
			{
				var position = new Float3(Random() * Radius, Random() * Height, 0f).RotateXZ(Random() * 360f);
				queries[i] = new Ray(position, (new Float3(0f, 1.2f, 0f) - position).Normalized);
			}

			Pairs = new[]
					{
						new Pair(new PressedScene(scene, new ScenePressProfile { AcceleratorProfile = new TraceAcceleratorProfile { AcceleratorType = typeof(BoundingVolumeHierarchy) } }), "Regular"),
						new Pair(new PressedScene(scene, new ScenePressProfile { AcceleratorProfile = new TraceAcceleratorProfile { AcceleratorType = typeof(QuadBoundingVolumeHierarchy) } }), "Quad")
					};

			float Random() => (float)random.NextDouble();
		}

		readonly HitQuery[] queries;

		[ParamsSource(nameof(Pairs))]
		public Pair CurrentPair { get; set; }

		public IEnumerable<Pair> Pairs { get; set; }

		//First test set. Different sets will have different timings
		//V0: 903.5us per 1000 intersections (recursive)
		//V1: 821.6us per 1000 intersections (iterative unsafe)
		//V2: 761.2us per 1000 intersections (iterative cached hit)

		//NOTE: Tests with 65536 rays will have a higher average because the rays are more distributed

		// |             Method |     Mean |    Error |   StdDev |
		// |------------------- |---------:|---------:|---------:|
		// | GetIntersectionNew | 54.96 ms | 0.265 ms | 0.248 ms |
		// |    GetIntersection | 58.26 ms | 0.252 ms | 0.223 ms |

		[Benchmark]
		public void GetIntersection()
		{
			for (int i = 0; i < queries.Length; i++)
			{
				ref HitQuery query = ref queries[i];
				CurrentPair.scene.GetIntersection(ref query);
				query.distance = float.PositiveInfinity;
			}
		}

		// [Benchmark]
		// public float GetIntersectionNew()
		// {
		// 	bool hit = default;
		//
		// 	for (int i = 0; i < rays.Length; i++) hit = pressed.GetIntersection(rays[i]);
		//
		// 	return hit ? 1f : 0f;
		// }

		// [Benchmark]
		// public float GetIntersection()
		// {
		// 	bool hit = default;
		// 	CalculatedHit calculated = default;
		//
		// 	for (int i = 0; i < rays.Length; i++) hit = pressed.GetIntersection(rays[i], out calculated);
		//
		// 	return hit ? calculated.distance : 0f;
		// }

		// [Benchmark]
		// public Hit GetIntersectionOld()
		// {
		// 	Hit hit = default;
		//
		// 	for (int i = 0; i < rays.Length; i++) hit = bvh.GetIntersectionOld(rays[i]);
		//
		// 	return hit;
		// }

		public readonly struct Pair
		{
			public Pair(PressedScene scene, string name)
			{
				this.scene = scene;
				this.name = name;
			}

			public readonly PressedScene scene;
			public readonly string name;

			public override string ToString() => name;
		}
	}

}