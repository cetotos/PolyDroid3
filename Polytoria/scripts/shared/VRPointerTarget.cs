// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System.Collections.Generic;

namespace Polytoria.Shared;

public interface IVRPointerTarget
{
	Transform3D PanelGlobalTransform { get; }
	Vector2 PanelSizeMeters { get; }
	Vector2I ViewportPixelSize { get; }
	SubViewport TargetViewport { get; }
	bool AcceptsPointer { get; }
	void OnPointerHit();
}

public static class VRPointerRegistry
{
	public static readonly List<IVRPointerTarget> Targets = new();

	public static void Register(IVRPointerTarget t)
	{
		if (!Targets.Contains(t)) Targets.Add(t);
	}

	public static void Unregister(IVRPointerTarget t)
	{
		Targets.Remove(t);
	}
}
