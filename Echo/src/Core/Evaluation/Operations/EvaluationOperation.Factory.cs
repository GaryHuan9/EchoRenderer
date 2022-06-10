﻿using System;
using System.Collections.Immutable;
using CodeHelpers;
using Echo.Common;
using Echo.Common.Compute;
using Echo.Common.Memory;
using Echo.Core.Evaluation.Distributions.Continuous;

namespace Echo.Core.Evaluation.Operations;

partial class EvaluationOperation
{
	/// <summary>
	/// An implementation of <see cref="IOperationFactory"/> for <see cref="EvaluationOperation"/>.
	/// </summary>
	public sealed class Factory : IOperationFactory
	{
		Context[] contexts; //cache contexts to reuse them

		NotNull<EvaluationProfile> _nextProfile;

		/// <summary>
		/// The next <see cref="EvaluationProfile"/> to use.
		/// </summary>
		/// <remarks>Cannot be null.</remarks>
		public EvaluationProfile NextProfile
		{
			get => _nextProfile;
			set => _nextProfile = value;
		}

		/// <inheritdoc/>
		public Operation CreateOperation(ImmutableArray<IWorker> workers)
		{
			//Validate profile
			var profile = NextProfile ?? throw ExceptionHelper.Invalid(nameof(NextProfile), InvalidType.isNull);
			profile.Validate();

			//Create contexts
			CreateContexts(profile, workers.Length);

			//Return new operation
			return new EvaluationOperation(profile, workers, contexts.ToImmutableArray());
		}

		void CreateContexts(EvaluationProfile profile, int population)
		{
			Utility.EnsureCapacity(ref contexts, population, true);
			ContinuousDistribution source = profile.Distribution;

			foreach (ref Context context in contexts.AsSpan(0, population))
			{
				if (context.Distribution != source) context = context with { Distribution = source with { } };
				if (context.Allocator == null) context = context with { Allocator = new Allocator() };
			}
		}
	}
}