﻿using System;
using CodeHelpers.Diagnostics;
using CodeHelpers.Mathematics;
using CodeHelpers.Packed;
using Echo.Core.Common.Mathematics;

namespace Echo.Core.Aggregation.Bounds;

public readonly struct ConeBounds
{
	ConeBounds(in Float3 axis, float cosOffset = 1f, float cosExtend = 0f)
	{
		Assert.AreEqual(axis.SquaredMagnitude, 1f);
		Assert.IsTrue(cosOffset is >= -1f and <= 1f);
		Assert.IsTrue(cosExtend is >= 0f and <= 1f);

		this.axis = axis;
		this.cosOffset = cosOffset;
		this.cosExtend = cosExtend;
	}

	public readonly Float3 axis;
	public readonly float cosOffset;
	public readonly float cosExtend;

	public float Area //relative
	{
		get
		{
			Assert.AreNotEqual(axis, default);

			float offset = MathF.Acos(FastMath.Clamp11(cosOffset));
			float extend = MathF.Acos(FastMath.Clamp11(cosExtend));

			float angle = FastMath.Min(offset + extend, Scalars.Pi) * 2f;
			float sinOffset = FastMath.Identity(cosOffset);

			return Scalars.Tau * (1f - cosOffset) + Scalars.Pi / 2f *
			(
				angle * sinOffset - MathF.Cos(offset - angle) -
				2f * offset * sinOffset + cosOffset
			);
		}
	}

	public ConeBounds Encapsulate(in ConeBounds other)
	{
		Assert.AreNotEqual(axis, default);
		Assert.AreNotEqual(other.axis, default);

		return other.cosOffset > cosOffset ? Union(this, other) : Union(other, this);
	}

	public static ConeBounds CreateFullSphere(float cosExtend = 0f /* = cos(pi/2) */) => new(Float3.Up, -1f /* = cos(pi) */, cosExtend);

	public static ConeBounds CreateDirection(in Float3 direction, float cosExtend = 0f /* = cos(pi/2) */) => new(direction, 1f /* = cos(0) */, cosExtend);

	public static ConeBounds operator *(in Float4x4 transform, ConeBounds bounds) => new
	(
		transform.MultiplyDirection(bounds.axis).Normalized,
		bounds.cosOffset, bounds.cosExtend
	);

	static ConeBounds Union(in ConeBounds value0, in ConeBounds value1)
	{
		float offset0 = MathF.Acos(FastMath.Clamp11(value0.cosOffset));
		float offset1 = MathF.Acos(FastMath.Clamp11(value1.cosOffset));
		float cosExtend = FastMath.Min(value0.cosExtend, value1.cosExtend);

		Assert.IsTrue(offset0 >= offset1);

		Float3 axis = value0.axis;
		float max = value0.axis.Angle(value1.axis) + offset1;

		//Early exit if the offset of value0 is large enough already and do not need to extended
		if (FastMath.Min(max, Scalars.Pi) <= offset0) return new ConeBounds(axis, value0.cosOffset, cosExtend);

		//Create new cone over the two inputs if the value0 is not large enough already
		float offset = offset0;
		offset = (offset + max) / 2f;

		//Clamp and return the maximum cone if offset is pi or larger
		if (offset >= Scalars.Pi) return CreateFullSphere(cosExtend);

		//Rotate axis to the middle of the two axes of the two inputs
		Float3 cross = axis.Cross(value1.axis);
		float rotation = offset - offset0;
		axis = new Versor(cross, rotation) * axis;

		return new ConeBounds(axis, MathF.Cos(offset), cosExtend);
	}
}