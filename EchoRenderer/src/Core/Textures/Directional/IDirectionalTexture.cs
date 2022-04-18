using System;
using System.Threading;
using System.Threading.Tasks;
using CodeHelpers.Packed;
using EchoRenderer.Common.Mathematics.Primitives;
using EchoRenderer.Common.Mathematics.Randomization;
using EchoRenderer.Core.Evaluation.Distributions;
using EchoRenderer.Core.Textures.Colors;

namespace EchoRenderer.Core.Textures.Directional;

/// <summary>
/// A special texture that can only be sampled based on directions.
/// </summary>
public interface IDirectionalTexture
{
	/// <summary>
	/// The average of this <see cref="IDirectionalTexture"/> across all directions.
	/// </summary>
	RGB128 Average { get; }

	/// <summary>
	/// Invoked prior to rendering begins to perform any initialization work this <see cref="IDirectionalTexture"/> need.
	/// Other members defined in this interface will/should not be used before this method is invoked at least once.
	/// </summary>
	virtual void Prepare() { }

	/// <summary>
	/// Evaluates this <see cref="IDirectionalTexture"/> at <paramref name="incident"/>.
	/// </summary>
	/// <param name="incident">The unit local direction to evaluate at.</param>
	/// <returns>The <see cref="RGB128"/> value evaluated.</returns>
	/// <seealso cref="Sample"/>
	RGB128 Evaluate(in Float3 incident);

	/// <summary>
	/// Calculates the pdf of selecting <paramref name="incident"/> with <see cref="Sample"/>.
	/// </summary>
	/// <param name="incident">The unit local direction that was selected.</param>
	/// <returns>The probability density function (pdf) value of the selection.</returns>
	/// <seealso cref="Sample"/>
	float ProbabilityDensity(in Float3 incident) => Sample2D.UniformSpherePdf;

	/// <summary>
	/// Samples <paramref name="incident"/> for this <see cref="IDirectionalTexture"/>.
	/// </summary>
	/// <param name="sample">The <see cref="Sample2D"/> used to sample <paramref name="incident"/>.</param>
	/// <param name="incident">The unit local direction specifically sampled for this texture.</param>
	/// <returns>The <see cref="Probable{T}"/> value evaluated at <paramref name="incident"/>.</returns>
	Probable<RGB128> Sample(Sample2D sample, out Float3 incident)
	{
		incident = sample.UniformSphere;
		return (Evaluate(incident), Sample2D.UniformSpherePdf);
	}
}

public static class IDirectionalTextureExtensions
{
	/// <summary>
	/// Explicitly calculates a converged value for <see cref="IDirectionalTexture.Average"/> using Monte Carlo sampling.
	/// </summary>
	public static RGB128 ConvergeAverage(this IDirectionalTexture texture, int sampleCount = (int)1E6)
	{
		using ThreadLocal<SumPackage> sums = new(SumPackage.factory, true);

		//Sample random directions
		Parallel.For(0, sampleCount, _ =>
		{
			// ReSharper disable once AccessToDisposedClosure
			SumPackage package = sums.Value;

			var direction = package.random.NextOnSphere();
			package.Sum += texture.Evaluate(direction);
		});

		//Total the sums for individual threads
		Summation sum = Summation.Zero;

		foreach (SumPackage package in sums.Values) sum += package.Sum;

		return (RGB128)(sum.Result / sampleCount);
	}

	class SumPackage
	{
		SumPackage()
		{
			random = new SquirrelRandom();
			Sum = Summation.Zero;
		}

		public readonly IRandom random;
		public Summation Sum { get; set; }

		public static readonly Func<SumPackage> factory = () => new SumPackage();
	}
}