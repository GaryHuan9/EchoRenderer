﻿using CodeHelpers.Mathematics;
using EchoRenderer.Mathematics;
using EchoRenderer.Mathematics.Intersections;
using EchoRenderer.Rendering.Materials;
using EchoRenderer.Rendering.Memory;

namespace EchoRenderer.Rendering.Pixels
{
	public class AlbedoPixelWorker : PixelWorker
	{
		public override Sample Render(Float2 uv, Arena arena)
		{
			PressedScene scene = arena.profile.Scene;
			ExtendedRandom random = arena.random;

			TraceQuery query = scene.camera.GetRay(uv, random);

			while (scene.Trace(ref query))
			{
				Interaction interaction = scene.Interact(query, out Material material);

				Float3 albedo = Utilities.ToFloat3(material.Albedo[interaction.texcoord]);
				if (!HitPassThrough(query, albedo, interaction.outgoingWorld)) return albedo; //Return intersected albedo color

				query = query.Next(query.ray.direction);
			}

			return scene.cubemap?.Sample(query.ray.direction) ?? Float3.zero;
		}
	}
}