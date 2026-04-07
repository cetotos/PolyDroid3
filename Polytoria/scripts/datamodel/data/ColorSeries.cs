// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Interfaces;
using Polytoria.Scripting;
using System;
using System.Collections.Generic;

namespace Polytoria.Datamodel.Data;

public readonly struct ColorSeries : IScriptObject, IData
{
	internal struct ColorPoint(float offset, Color color)
	{
		public float Offset = offset;
		public Color Color = color;
	}

	private readonly List<ColorPoint> points;

	[ScriptProperty]
	public readonly int PointCount => points?.Count ?? 0;

	internal List<ColorPoint> Points => points;

	public ColorSeries()
	{
		points =
		[
			new(0f, new Color(0, 0, 0, 1)),
			new ColorPoint(1f, new Color(1, 1, 1, 1))
		];
	}

	[ScriptMethod]
	public static ColorSeries New()
	{
		return new();
	}

	[ScriptMethod]
	public static ColorSeries New(Color min, Color max)
	{
		ColorSeries r = new();
		r.points.Clear();
		r.points.Add(new ColorPoint(0f, min));
		r.points.Add(new ColorPoint(1f, max));
		return r;
	}

	[ScriptMethod]
	public readonly void Clear()
	{
		points.Clear();
	}

	[ScriptMethod]
	public readonly void SetColor(int point, Color color)
	{
		if (point < 0 || point >= points.Count)
			throw new ArgumentOutOfRangeException(nameof(point));

		points[point] = new ColorPoint(points[point].Offset, color);
	}

	[ScriptMethod]
	public readonly void RemovePoint(int point)
	{
		if (point < 0 || point >= points.Count)
			throw new ArgumentOutOfRangeException(nameof(point));

		points.RemoveAt(point);
	}

	[ScriptMethod]
	public readonly float[] GetOffsets()
	{
		List<float> offsets = [];
		foreach (ColorPoint p in points)
		{
			offsets.Add(p.Offset);
		}
		return [.. offsets];
	}

	[ScriptMethod]
	public readonly Color[] GetColors()
	{
		List<Color> clr = [];
		foreach (ColorPoint p in points)
		{
			clr.Add(p.Color);
		}
		return [.. clr];
	}

	[ScriptMethod]
	public readonly void SetOffset(int point, float offset)
	{
		ColorPoint p = new()
		{
			Offset = offset
		};
		if (point <= 0)
		{
			p.Color = new(0, 0, 0);
			points.Insert(0, p);
		}
		else if (point >= points.Count)
		{
			p.Color = new(0, 0, 0);
			points.Add(p);
		}
		else
		{
			p.Color = points[point].Color;
			points[point] = p;
		}

		SortPoints();
	}

	[ScriptMethod]
	public readonly Color GetColor(int point)
	{
		if (point < 0 || point >= points.Count)
			throw new ArgumentOutOfRangeException(nameof(point));

		return points[point].Color;
	}

	[ScriptMethod]
	public readonly float GetOffset(int point)
	{
		if (point < 0 || point >= points.Count)
			throw new ArgumentOutOfRangeException(nameof(point));

		return points[point].Offset;
	}

	[ScriptMethod]
	public readonly int AddPoint(float offset, Color color)
	{
		offset = Math.Clamp(offset, 0f, 1f);
		ColorPoint newPoint = new(offset, color);
		points.Add(newPoint);
		SortPoints();

		// Return the new index after sorting
		for (int i = 0; i < points.Count; i++)
		{
			if (points[i].Offset == offset && points[i].Color == color)
				return i;
		}
		return points.Count - 1;
	}

	[ScriptMethod]
	public readonly Color Lerp(float t)
	{
		if (points.Count == 0)
			return new Color(0, 0, 0, 1);

		if (points.Count == 1)
			return points[0].Color;

		t = Math.Clamp(t, 0f, 1f);

		for (int i = 0; i < points.Count - 1; i++)
		{
			if (t >= points[i].Offset && t <= points[i + 1].Offset)
			{
				float localT = (t - points[i].Offset) / (points[i + 1].Offset - points[i].Offset);
				return LerpColor(points[i].Color, points[i + 1].Color, localT);
			}
		}

		return points[^1].Color;
	}

	private readonly void SortPoints()
	{
		points.Sort((a, b) => a.Offset.CompareTo(b.Offset));
	}

	private static Color LerpColor(Color a, Color b, float t)
	{
		return a.Lerp(b, t);
	}

	public readonly Gradient ToGradient()
	{
		Gradient gradient = new();

		if (points == null || points.Count == 0)
		{
			// Return a default gradient if no points exist
			gradient.SetColor(0, new(0, 0, 0, 1));
			gradient.SetOffset(0, 0f);
			return gradient;
		}

		// Handle single point case
		if (points.Count == 1)
		{
			gradient.SetColor(0, points[0].Color);
			gradient.SetOffset(0, 0f);
			gradient.SetColor(1, points[0].Color);
			gradient.SetOffset(1, 1f);
			return gradient;
		}

		// Override two default points
		gradient.SetColor(0, points[0].Color);
		gradient.SetOffset(0, points[0].Offset);
		gradient.SetColor(1, points[1].Color);
		gradient.SetOffset(1, points[1].Offset);

		// Add remaining points
		for (int i = 2; i < points.Count; i++)
		{
			gradient.AddPoint(points[i].Offset, points[i].Color);
		}

		return gradient;
	}

	public readonly GradientTexture1D ToGradientTexture1D()
	{
		return new() { Gradient = ToGradient() };
	}

	public readonly Curve ToAlphaCurve()
	{
		Curve curve = new();

		if (points == null || points.Count == 0)
		{
			// if no points exist, return default
			return curve;
		}

		// Handle single point case
		if (points.Count == 1)
		{
			curve.SetPointValue(0, points[0].Color.A);
			curve.SetPointOffset(0, points[0].Offset);
			return curve;
		}

		foreach (ColorPoint p in points)
		{
			curve.AddPoint(new(p.Offset, p.Color.A));
		}

		return curve;
	}

	public override readonly int GetHashCode()
	{
		return HashCode.Combine(points);
	}

	public object Clone()
	{
		ColorSeries c = new();
		c.points.Clear();
		foreach (ColorPoint p in points)
		{
			c.points.Add(p);
		}
		return c;
	}
}
