﻿using System;
using CodeHelpers.Vectors;

namespace ForceRenderer.IO
{
	public class Material
	{
		public Float3 Albedo { get; set; }
		public Float3 Specular { get; set; }

		public Float3 Emission { get; set; }
		public float Smoothness { get; set; }
	}

	public readonly struct PressedMaterial
	{
		public PressedMaterial(Material material)
		{
			albedo = material.Albedo.Clamp(0f, 1f);
			specular = material.Specular.Clamp(0f, 1f);

			albedo = albedo.Min(Float3.one - specular); //Albedo and specular combined cannot be larger than one
			emission = material.Emission;

			// phongAlpha = MathF.Pow(3E8f, MathF.Pow(material.Smoothness.Clamp(0f, 1f), 1.5f)) - 1f;
			// phongAlpha = MathF.Pow(material.Smoothness.Clamp(0f, 1f), 20f) * 3E8f;
			phongAlpha = MathF.Pow(1200f, material.Smoothness.Clamp(0f, 1f) * 1.3f) - 1f;
			phongMultiplier = (phongAlpha + 2f) / (phongAlpha + 1f);

			//If specular is high, albedo should be contained to give more chance for specular reflection
			diffuseChance = albedo.Average;
			specularChance = specular.Average;

			float sum = diffuseChance + specularChance;

			if (Scalars.AlmostEquals(sum, 0f))
			{
				diffuseChance = 1f;
				specularChance = 0f;
			}
			else
			{
				diffuseChance /= sum;
				specularChance /= sum;
			}
		}

		public readonly Float3 albedo;
		public readonly Float3 specular;

		public readonly Float3 emission;
		public readonly float phongAlpha;
		public readonly float phongMultiplier;

		public readonly float diffuseChance;
		public readonly float specularChance;
	}
}